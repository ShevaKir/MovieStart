using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Media;

/// <param name="Audio">Audio tracks with ids as mpv numbers them: 1, 2, … in file order.</param>
public sealed record ProbeResult(double Duration, IReadOnlyList<MediaTrack> Audio, IReadOnlyList<MediaTrack> Subtitles);

public interface IMediaProbe
{
    /// <summary>Reads tracks and duration; null when the file cannot be read.</summary>
    Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Runs <c>ffprobe</c> (part of ffmpeg) and reads its JSON output.</summary>
public sealed class FfprobeMediaProbe(IOptions<MediaOptions> options, ILogger<FfprobeMediaProbe> logger) : IMediaProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private bool _reportedMissing;

    public async Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        var start = new ProcessStartInfo(options.Value.FfprobePath)
        {
            ArgumentList = { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("ffprobe did not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!_reportedMissing)
                logger.LogWarning("ffprobe is not available ({Path}); audio tracks will not be detected", options.Value.FfprobePath);
            _reportedMissing = true;
            return null;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(Timeout);
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0 ? Parse(await output) : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                logger.LogWarning("ffprobe timed out on {Path}", path);
                return null;
            }
        }
    }

    internal static ProbeResult? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var duration = root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var d)
                && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    ? seconds
                    : 0;

            var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
            return new ProbeResult(duration, Tracks(streams, "audio"), Tracks(streams, "subtitle"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<MediaTrack> Tracks(List<JsonElement> streams, string type) =>
        streams
            .Where(stream => stream.TryGetProperty("codec_type", out var t) && t.GetString() == type)
            .Select((stream, index) => new MediaTrack(index + 1, Tag(stream, "language"), Tag(stream, "title")))
            .ToList();

    private static string? Tag(JsonElement stream, string name) =>
        stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty(name, out var value)
            && value.GetString() is { Length: > 0 } text && text != "und"
            ? text
            : null;
}
