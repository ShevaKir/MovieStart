using MovieStart.Agent.Player;

namespace MovieStart.Agent.Library;

/// <summary>Plays the next downloaded episode of the season when one ends.</summary>
public sealed class EpisodeAutoplay(IPlayer player, LibraryService library, ILogger<EpisodeAutoplay> logger) : IHostedService
{
    internal static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(200);

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
            // mpv reports the end before it has finished closing the file; a file loaded in between
            // is put on the playlist but never started, so wait until mpv is idle.
            if (!await WaitUntilIdleAsync(player, IdleTimeout, _stopping.Token))
            {
                logger.LogWarning("The player did not go idle after {Path}; not playing the next episode", path);
                return;
            }

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

    /// <summary>Waits until nothing is loaded; false when that takes longer than <paramref name="timeout"/>.</summary>
    internal static async Task<bool> WaitUntilIdleAsync(IPlayer player, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (!player.State.IsIdle)
                await Task.Delay(IdlePollInterval, deadline.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
