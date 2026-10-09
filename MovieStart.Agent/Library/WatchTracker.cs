using MovieStart.Agent.Player;

namespace MovieStart.Agent.Library;

/// <summary>Records how far each file has been watched so playback can continue later.</summary>
public sealed class WatchTracker(IPlayer player, LibraryService library, ILogger<WatchTracker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var state = player.State;
            if (!state.IsConnected || state.IsIdle || state.FilePath is null || state.Duration <= 0)
                continue;

            try
            {
                await library.RecordProgressAsync(state.FilePath, state.Position, state.Duration, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not record watch progress");
            }
        }
    }
}
