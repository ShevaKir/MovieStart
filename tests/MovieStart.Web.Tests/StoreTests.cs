using System.Net;
using MovieStart.Shared;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;
using MovieStart.Web.State;

namespace MovieStart.Web.Tests;

public sealed class StoreTests
{
    private static readonly LibraryItem Dune = new() { Id = Guid.NewGuid(), Title = "Dune" };

    private readonly FakeAgent _agent = new();
    private readonly Notifications _notifications = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LibraryStore Library() => new(_agent.Client, _agent.Address, _notifications);

    private PlayerStore Player() => new(_agent.Client, _agent.Address, _notifications);

    [Fact]
    public async Task LibraryLoadsItemsAndStorage()
    {
        _agent.Reply("GET", ApiRoutes.Library, new[] { Dune });
        _agent.Reply("GET", ApiRoutes.Storage, new StorageInfo(100, 40));
        var library = Library();
        var changes = 0;
        library.Changed += () => changes++;

        await library.RefreshAsync(Token);

        Assert.True(library.IsLoaded);
        Assert.True(library.IsOnline);
        Assert.Equal("Dune", Assert.Single(library.Items).Title);
        Assert.Equal(40, library.Storage?.FreeBytes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LibraryKeepsItsItemsWhenThePiGoesOffline()
    {
        _agent.Reply("GET", ApiRoutes.Library, new[] { Dune });
        var library = Library();
        await library.RefreshAsync(Token);

        _agent.Fail("GET", ApiRoutes.Library, HttpStatusCode.BadGateway, "down");
        await library.RefreshAsync(Token);

        Assert.False(library.IsOnline);
        Assert.Single(library.Items);
    }

    [Fact]
    public async Task PlayingAsksTheAgentForTheChosenFile()
    {
        _agent.Reply("GET", ApiRoutes.Library, new[] { Dune });
        _agent.Reply("POST", ApiRoutes.LibraryPlay(Dune.Id), new MediaFile(3, Guid.NewGuid(), "Dune.mkv", 1));

        await Library().PlayAsync(Dune.Id, new PlayItemRequest(3, FromStart: true));

        var play = Assert.Single(_agent.Requests, request => request.Request.StartsWith("POST"));
        Assert.Contains("\"fileId\":3", play.Body);
        Assert.Contains("\"fromStart\":true", play.Body);
        Assert.Null(_notifications.Message);
    }

    [Fact]
    public async Task FailedCommandIsShownToTheViewer()
    {
        _agent.Fail("POST", ApiRoutes.PlayerCommand, HttpStatusCode.ServiceUnavailable, "The player is not running.");

        await Player().TogglePauseAsync();

        Assert.Equal("The player is not running.", _notifications.Message);
    }

    [Fact]
    public async Task PauseTogglesFromTheCurrentState()
    {
        _agent.Reply("GET", ApiRoutes.PlayerState, new PlayerState { IsConnected = true, IsIdle = false, IsPaused = true });
        var player = Player();
        await player.RefreshAsync(Token);

        await player.TogglePauseAsync();

        var command = Assert.Single(_agent.Requests, request => request.Request.StartsWith("POST"));
        Assert.Contains("\"Resume\"", command.Body);
        Assert.True(player.HasMedia);
    }

    [Fact]
    public async Task UnreachablePlayerReadsAsDisconnected()
    {
        var player = Player();

        await player.RefreshAsync(Token);

        Assert.False(player.State.IsConnected);
        Assert.False(player.HasMedia);
    }
}
