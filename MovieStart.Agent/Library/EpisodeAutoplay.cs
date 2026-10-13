using MovieStart.Agent.Player;

namespace MovieStart.Agent.Library;

/// <summary>Plays the next downloaded episode of the season when one ends.</summary>
public sealed class EpisodeAutoplay(IPlayer player, LibraryService library, ILogger<EpisodeAutoplay> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        player.PlaybackFinished += OnPlaybackFinished;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        player.PlaybackFinished -= OnPlaybackFinished;
        await _stopping.CancelAsync();
    }

    // Raised on the player's reader thread, which must not wait for the next file to load.
    private void OnPlaybackFinished(string path) => _ = PlayNextAsync(path);

    internal async Task PlayNextAsync(string path)
    {
        try
        {
            if (await library.PlayNextEpisodeAsync(path, _stopping.Token) is { } next)
                logger.LogInformation("Playing the next episode {Episode}", next.EpisodeCode);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not play the next episode after {Path}", path);
        }
    }
}
