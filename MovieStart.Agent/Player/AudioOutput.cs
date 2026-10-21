using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MovieStart.Agent.Player;

public interface IAudioOutput
{
    /// <summary>Brings the TV's audio output back if the sound server has lost it.</summary>
    /// <returns>True when the sound server was restarted, so the player has to reopen its audio output.</returns>
    Task<bool> RestoreAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Watches the default PipeWire sink. When the TV goes off, WirePlumber turns the HDMI card's profile off
/// and does not turn it back on once the TV is back, leaving only its dummy sink; a WirePlumber restart
/// probes the card again. Needs <c>XDG_RUNTIME_DIR</c> and <c>DBUS_SESSION_BUS_ADDRESS</c> of the desktop session.
/// </summary>
public sealed partial class PipeWireAudioOutput(ILogger<PipeWireAudioOutput> logger) : IAudioOutput
{
    // WirePlumber's fallback sink when no real output exists.
    internal const string DummySink = "auto_null";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SinkWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private bool _reportedMissing;

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (await DefaultSinkAsync(cancellationToken) != DummySink)
            return false;

        logger.LogWarning("Only the dummy audio sink is left; restarting WirePlumber");
        if (await RunAsync("systemctl", ["--user", "restart", "wireplumber"], cancellationToken) is null)
            return false;

        var deadline = DateTime.UtcNow + SinkWait;
        string? sink;
        do
        {
            await Task.Delay(PollInterval, cancellationToken);
            sink = await DefaultSinkAsync(cancellationToken);
        } while (sink is null or DummySink && DateTime.UtcNow < deadline);

        if (sink is null or DummySink)
            logger.LogWarning("Still no audio output after restarting WirePlumber; the TV may be off");
        else
            logger.LogInformation("Audio output is back: {Sink}", sink);
        return true;
    }

    private async Task<string?> DefaultSinkAsync(CancellationToken cancellationToken) =>
        await RunAsync("wpctl", ["inspect", "@DEFAULT_AUDIO_SINK@"], cancellationToken) is { } output
            ? ParseNodeName(output)
            : null;

    internal static string? ParseNodeName(string inspectOutput) =>
        NodeName().Match(inspectOutput) is { Success: true } match ? match.Groups["name"].Value : null;

    [GeneratedRegex(@"node\.name = ""(?<name>[^""]*)""")]
    private static partial Regex NodeName();

    /// <summary>Standard output of a successful run; null when the tool is missing, fails or hangs.</summary>
    private async Task<string?> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Not a PipeWire system, e.g. a Mac during development.
            if (!_reportedMissing)
                logger.LogInformation("{Tool} is not available; the audio output is not checked", fileName);
            _reportedMissing = true;
            return null;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(CommandTimeout);
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode == 0)
                    return await output;

                logger.LogWarning("{Tool} failed: {Error}", fileName, (await error).Trim());
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("{Tool} timed out", fileName);
                return null;
            }
        }
    }
}
