using MovieStart.Shared.Player;

namespace MovieStart.Agent.Player;

/// <summary>
/// Reloads the file at the current position when playback stops moving without being paused.
/// A flaky drive can fail a read mid-film; mpv then loses its audio output and freezes for good,
/// while loading the file again reads fine.
/// </summary>
public sealed class PlaybackWatchdog(IPlayer player, TimeProvider time, ILogger<PlaybackWatchdog> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Restarts in a row without progress before giving up on the file.</summary>
    internal const int MaxRestarts = 3;

    /// <summary>Resume a little earlier so the moment of the freeze is not lost.</summary>
    internal const double RewindSeconds = 3;

    // Near the end mpv may stop at the last frame on its own.
    private const double EndMarginSeconds = 5;

    private string? _path;
    private double _position;
    private DateTimeOffset _since;
    private int _restarts;
    private double _stalledAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is PlayerUnavailableException or PlayerCommandException)
            {
                logger.LogWarning("Could not restart stalled playback: {Reason}", ex.Message);
            }
        }
    }

    internal async Task CheckAsync(CancellationToken cancellationToken)
    {
        var state = player.State;
        var now = time.GetUtcNow();

        // A new file, or playing past the last freeze, means the last restart worked.
        if (state.FilePath != _path || state.Position > _stalledAt + 1)
            _restarts = 0;

        if (!IsPlaying(state))
        {
            // Paused or stopped: the clock starts again once playback resumes.
            Reset(state.FilePath, state.Position, now);
            return;
        }

        if (state.FilePath != _path || state.Position != _position)
        {
            Reset(state.FilePath, state.Position, now);
            return;
        }

        if (now - _since < StallTimeout || _restarts >= MaxRestarts)
            return;

        _restarts++;
        _stalledAt = state.Position;
        _since = now;
        var start = Math.Max(0, state.Position - RewindSeconds);
        logger.LogWarning(
            "Playback of {Path} stalled at {Position:0}s, reloading (attempt {Attempt} of {Max})",
            state.FilePath, state.Position, _restarts, MaxRestarts);

        await player.PlayAsync(
            state.FilePath!,
            new PlaybackOptions(start, state.AudioTrackId, new SubtitleChoice(state.SubtitleTrackId)),
            cancellationToken);
    }

    private static bool IsPlaying(PlayerState state) =>
        state is { IsConnected: true, IsIdle: false, IsPaused: false, FilePath: not null }
        && state.Duration > 0
        && state.Position < state.Duration - EndMarginSeconds;

    private void Reset(string? path, double position, DateTimeOffset now)
    {
        _path = path;
        _position = position;
        _since = now;
    }
}
