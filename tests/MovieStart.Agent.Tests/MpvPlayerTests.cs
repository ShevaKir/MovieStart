using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class MpvPlayerTests : IAsyncLifetime
{
    private readonly FakeMpvServer _mpv = new();
    private MpvPlayer _player = null!;

    public async ValueTask InitializeAsync()
    {
        _player = new MpvPlayer(
            Options.Create(new PlayerOptions { SocketPath = _mpv.SocketPath }),
            NullLogger<MpvPlayer>.Instance);
        await _player.StartAsync(TestContext.Current.CancellationToken);
        await Eventually.AssertAsync(() => _player.State.IsConnected, "the player connects to mpv");
        await Eventually.AssertAsync(() => _mpv.Commands.Count(c => Name(c) == "observe_property") == 10, "all properties are observed");
    }

    public async ValueTask DisposeAsync()
    {
        await _player.StopAsync(CancellationToken.None);
        _player.Dispose();
        await _mpv.DisposeAsync();
    }

    [Fact]
    public async Task PlayLoadsFileAndUnpauses()
    {
        await _player.PlayAsync("/mnt/movies/Dune.mkv", TestContext.Current.CancellationToken);

        var commands = _mpv.Commands.Where(c => Name(c) != "observe_property").Select(Render).ToList();
        Assert.Equal(["loadfile /mnt/movies/Dune.mkv replace", "set_property pause False"], commands);
    }

    [Fact]
    public async Task MirrorsPropertyChangesIntoState()
    {
        await _mpv.SendPropertyChangeAsync("idle-active", false);
        await _mpv.SendPropertyChangeAsync("media-title", "Dune");
        await _mpv.SendPropertyChangeAsync("pause", true);
        await _mpv.SendPropertyChangeAsync("time-pos", 61.5);
        await _mpv.SendPropertyChangeAsync("duration", 9300.0);
        await _mpv.SendPropertyChangeAsync("volume", 70.0);
        await _mpv.SendPropertyChangeAsync("aid", 2);
        await _mpv.SendPropertyChangeAsync("sid", false);
        await _mpv.SendPropertyChangeAsync("track-list", new object[]
        {
            new { id = 1, type = "video" },
            new { id = 1, type = "audio", lang = "ukr", title = "Dub" },
            new { id = 2, type = "audio", lang = "eng" },
            new { id = 1, type = "sub", lang = "rus", title = "Forced" },
        });

        await Eventually.AssertAsync(() => _player.State.SubtitleTracks.Count == 1, "the track list is applied");

        var state = _player.State;
        Assert.False(state.IsIdle);
        Assert.Equal("Dune", state.Title);
        Assert.True(state.IsPaused);
        Assert.Equal(61.5, state.Position);
        Assert.Equal(9300, state.Duration);
        Assert.Equal(70, state.Volume);
        Assert.Equal(2, state.AudioTrackId);
        Assert.Null(state.SubtitleTrackId);
        Assert.Equal([new MediaTrack(1, "ukr", "Dub"), new MediaTrack(2, "eng", null)], state.AudioTracks);
        Assert.Equal([new MediaTrack(1, "rus", "Forced")], state.SubtitleTracks);
    }

    [Fact]
    public async Task UnavailablePropertyResetsValue()
    {
        await _mpv.SendPropertyChangeAsync("time-pos", 10.0);
        await Eventually.AssertAsync(() => _player.State.Position == 10, "position is set");

        await _mpv.SendEventAsync(new { @event = "property-change", id = 1, name = "time-pos" });

        await Eventually.AssertAsync(() => _player.State.Position == 0, "position is reset");
    }

    [Fact]
    public async Task RejectedCommandThrows()
    {
        _mpv.FailCommand("seek", "property unavailable");

        var ex = await Assert.ThrowsAsync<PlayerCommandException>(
            () => _player.SendAsync(PlayerCommand.SeekRelative(30), TestContext.Current.CancellationToken));
        Assert.Contains("property unavailable", ex.Message);
    }

    [Fact]
    public async Task ReportsDisconnectAndReconnects()
    {
        _mpv.DropConnection();

        await Eventually.AssertAsync(() => !_player.State.IsConnected, "the drop is noticed");
        await Assert.ThrowsAsync<PlayerUnavailableException>(
            () => _player.SendAsync(PlayerCommand.Stop(), TestContext.Current.CancellationToken));
        await Eventually.AssertAsync(() => _player.State.IsConnected, "the player reconnects", timeoutMs: 5000);
    }

    [Theory]
    [MemberData(nameof(CommandMappings))]
    public void MapsCommandsToMpv(PlayerCommand command, string expected)
    {
        var mpvCommand = MpvPlayer.ToMpvCommand(command);

        Assert.Equal(expected, string.Join(' ', mpvCommand));
    }

    public static TheoryData<PlayerCommand, string> CommandMappings => new()
    {
        { PlayerCommand.Pause(), "set_property pause True" },
        { PlayerCommand.Resume(), "set_property pause False" },
        { PlayerCommand.SeekRelative(-30), "seek -30 relative" },
        { PlayerCommand.SeekAbsolute(600), "seek 600 absolute" },
        { PlayerCommand.SetVolume(150), "set_property volume 100" },
        { PlayerCommand.SetAudioTrack(2), "set_property aid 2" },
        { PlayerCommand.SetSubtitleTrack(3), "set_property sid 3" },
        { PlayerCommand.SetSubtitleTrack(null), "set_property sid no" },
        { PlayerCommand.Stop(), "stop" },
    };

    [Theory]
    [InlineData(PlayerCommandType.SeekRelative)]
    [InlineData(PlayerCommandType.SetVolume)]
    [InlineData(PlayerCommandType.SetAudioTrack)]
    public void CommandsWithoutRequiredValueAreRejected(PlayerCommandType type)
    {
        Assert.Throws<ArgumentException>(() => MpvPlayer.ToMpvCommand(new PlayerCommand(type)));
    }

    private static string Name(JsonElement[] command) => command[0].GetString()!;

    private static string Render(JsonElement[] command) =>
        string.Join(' ', command.Select(e => e.ValueKind switch
        {
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => e.ToString(),
        }));
}
