using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MovieStart.Agent.Player;
using MovieStart.Shared;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class PlayerEndpointsTests : IDisposable
{
    private readonly string _mediaRoot = Directory.CreateTempSubdirectory("movies-").FullName;
    private readonly FakePlayer _player = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PlayerEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Player:MediaRoot", _mediaRoot);
            builder.UseSetting("Player:SocketPath", Path.Combine(_mediaRoot, "none.sock"));
            builder.ConfigureTestServices(services => services.AddSingleton<IPlayer>(_player));
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_mediaRoot, recursive: true);
    }

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
        var file = Path.Combine(_mediaRoot, "Dune.mkv");
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
            ApiRoutes.PlayerPlay, new PlayRequest(Path.Combine(_mediaRoot, "missing.mkv")), TestContext.Current.CancellationToken);

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

    private sealed class FakePlayer : IPlayer
    {
        public PlayerState State { get; set; } = PlayerState.Disconnected;

        public string? PlayedPath { get; private set; }

        public PlayerCommand? LastCommand { get; private set; }

        public Exception? Failure { get; set; }

        public Task PlayAsync(string path, CancellationToken cancellationToken)
        {
            if (Failure is not null)
                throw Failure;
            PlayedPath = path;
            return Task.CompletedTask;
        }

        public Task SendAsync(PlayerCommand command, CancellationToken cancellationToken)
        {
            if (Failure is not null)
                throw Failure;
            LastCommand = command;
            return Task.CompletedTask;
        }
    }
}
