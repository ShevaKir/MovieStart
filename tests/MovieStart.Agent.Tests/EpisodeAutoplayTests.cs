using MovieStart.Agent.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class EpisodeAutoplayTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PlayerState Playing => new() { IsConnected = true, IsIdle = false, FilePath = "/mnt/movies/S01E01.mkv" };

    [Fact]
    public async Task WaitsUntilThePlayerHasClosedTheFinishedFile()
    {
        var player = new FakePlayer { State = Playing };

        var waiting = EpisodeAutoplay.WaitUntilIdleAsync(player, TimeSpan.FromSeconds(5), Token);
        await Task.Delay(300, Token);
        Assert.False(waiting.IsCompleted);

        player.State = PlayerState.Disconnected with { IsConnected = true };
        Assert.True(await waiting);
    }

    [Fact]
    public async Task GivesUpWhenThePlayerStaysBusy()
    {
        var player = new FakePlayer { State = Playing };

        Assert.False(await EpisodeAutoplay.WaitUntilIdleAsync(player, TimeSpan.FromMilliseconds(300), Token));
    }
}
