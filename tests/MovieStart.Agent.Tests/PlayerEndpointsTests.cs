using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MovieStart.Agent.Player;
using MovieStart.Shared;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class PlayerEndpointsTests : IDisposable
{
    private readonly FakePlayer _player = new();
    private readonly AgentFactory _factory;

    public PlayerEndpointsTests()
    {
        _factory = new AgentFactory(services => services.AddSingleton<IPlayer>(_player));
    }

    private string MediaRoot => _factory.MediaRoot;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ReturnsPlayerState()
    {
        _player.State = new PlayerState { IsConnected = true, IsIdle = false, Title = "Dune", Position = 12 };
        using var client = _factory.CreateClient();

        var state = await client.GetFromJsonAsync<PlayerState>(ApiRoutes.PlayerState, TestContext.Current.CancellationToken);

        Assert.NotNull(state);
        Assert.True(state.IsConnected);
        Assert.False(state.IsIdle);
        Assert.Equal("Dune", state.Title);
        Assert.Equal(12, state.Position);
    }

    [Fact]
    public async Task PlaysFileInsideMediaRoot()
    {
        var file = Path.Combine(MediaRoot, "Dune.mkv");
        await File.WriteAllTextAsync(file, "", TestContext.Current.CancellationToken);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.PlayerPlay, new PlayRequest(file), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(file, _player.PlayedPath);
    }

    [Fact]
    public async Task RejectsFileOutsideMediaRoot()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.PlayerPlay, new PlayRequest("/etc/hosts"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(_player.PlayedPath);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingFile()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            ApiRoutes.PlayerPlay, new PlayRequest(Path.Combine(MediaRoot, "missing.mkv")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsCommandWithEnumAsString()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            ApiRoutes.PlayerCommand,
            new StringContent("""{"type":"SeekRelative","value":-30}""", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(PlayerCommand.SeekRelative(-30), _player.LastCommand);
    }

    [Theory]
    [InlineData(typeof(PlayerUnavailableException), HttpStatusCode.ServiceUnavailable)]
    [InlineData(typeof(ArgumentException), HttpStatusCode.BadRequest)]
    public async Task MapsPlayerErrorsToStatusCodes(Type exceptionType, HttpStatusCode expected)
    {
        _player.Failure = (Exception)Activator.CreateInstance(exceptionType)!;
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.PlayerCommand, PlayerCommand.Stop(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MapsRejectedCommandToConflict()
    {
        _player.Failure = new PlayerCommandException("seek", "property unavailable");
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ApiRoutes.PlayerCommand, PlayerCommand.SeekRelative(30), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
