using MovieStart.Agent.Downloads;

namespace MovieStart.Agent.Library;

/// <summary>Loads the library on start and keeps download progress in sync with qBittorrent.</summary>
public sealed class LibraryWatcher(LibraryService library, ILogger<LibraryWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await library.LoadAsync(stoppingToken);

        var reportedUnavailable = false;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await library.SyncAsync(stoppingToken);
                reportedUnavailable = false;
            }
            catch (QbitUnavailableException ex)
            {
                if (!reportedUnavailable)
                    logger.LogWarning("Cannot sync downloads: {Reason}", ex.Message);
                reportedUnavailable = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep syncing; a single bad movie or disk hiccup must not stop the agent.
                logger.LogError(ex, "Download sync failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
