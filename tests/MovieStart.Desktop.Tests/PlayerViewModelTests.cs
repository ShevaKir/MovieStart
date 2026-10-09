using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.Tests;

public class PlayerViewModelTests
{
    private readonly FakeAgentClient _agent = new();
    private readonly PlayerViewModel _player;

    public PlayerViewModelTests()
    {
        _player = new PlayerViewModel(_agent, () => "http://pi.local:5080", debounce: TimeSpan.Zero);
    }

    private static PlayerState Playing => new()
    {
        IsConnected = true,
        IsIdle = false,
        Title = "Dune",
        Position = 4324,
        Duration = 9300,
        Volume = 70,
        AudioTracks = [new MediaTrack(1, "ukr", "Dub"), new MediaTrack(2, "eng", null)],
        AudioTrackId = 1,
        SubtitleTracks = [new MediaTrack(3, "rus", null)],
        SubtitleTrackId = null,
    };

    [Fact]
    public async Task RefreshAppliesAgentState()
    {
        _agent.PlayerState = Playing;

        await _player.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(_player.HasMedia);
        Assert.Equal("Dune", _player.Title);
        Assert.Equal("1:12:04", _player.PositionText);
        Assert.Equal("2:35:00", _player.DurationText);
        Assert.Equal(70, _player.Volume);
        Assert.Equal(["UKR · Dub", "ENG"], _player.AudioTracks.Select(t => t.Label));
        Assert.Equal(1, _player.SelectedAudioTrack?.Id);
        Assert.Equal(["Off", "RUS"], _player.SubtitleTracks.Select(t => t.Label));
        Assert.Same(TrackOption.Off, _player.SelectedSubtitleTrack);
    }

    [Fact]
    public async Task UnreachableAgentShowsDisconnected()
    {
        _player.Apply(Playing);
        _agent.PlayerState = null;

        await _player.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(_player.IsConnected);
        Assert.False(_player.HasMedia);
        Assert.Equal("Nothing playing", _player.Title);
    }

    [Fact]
    public void ApplyingStateDoesNotSendCommands()
    {
        _player.Apply(Playing);
        _player.Apply(Playing with { Position = 10, Volume = 40, AudioTrackId = 2, SubtitleTrackId = 3 });

        Assert.Empty(_agent.Commands);
    }

    [Fact]
    public void KeepsTrackListsWhenTheyDidNotChange()
    {
        _player.Apply(Playing);
        var audio = _player.AudioTracks;

        _player.Apply(Playing with { Position = 10 });

        Assert.Same(audio, _player.AudioTracks);
    }

    [Theory]
    [InlineData(false, PlayerCommandType.Pause)]
    [InlineData(true, PlayerCommandType.Resume)]
    public async Task TogglePauseSendsTheOppositeCommand(bool paused, PlayerCommandType expected)
    {
        _player.Apply(Playing with { IsPaused = paused });

        await _player.TogglePauseCommand.ExecuteAsync(null);

        Assert.Equal(expected, Assert.Single(_agent.Commands).Type);
    }

    [Fact]
    public async Task SeekButtonsMoveThirtySeconds()
    {
        await _player.SeekBackCommand.ExecuteAsync(null);
        await _player.SeekForwardCommand.ExecuteAsync(null);

        Assert.Equal([PlayerCommand.SeekRelative(-30), PlayerCommand.SeekRelative(30)], _agent.Commands);
    }

    [Fact]
    public async Task MovingThePositionSliderSeeks()
    {
        _player.Apply(Playing);

        _player.Position = 600;
        await _player.WhenSentAsync();

        Assert.Equal(PlayerCommand.SeekAbsolute(600), Assert.Single(_agent.Commands));
    }

    [Fact]
    public async Task OnlyTheLastSliderValueIsSent()
    {
        var player = new PlayerViewModel(_agent, () => "http://pi.local:5080", debounce: TimeSpan.FromMilliseconds(50));
        player.Apply(Playing);

        player.Volume = 10;
        player.Volume = 20;
        player.Volume = 30;
        await player.WhenSentAsync();

        Assert.Equal(PlayerCommand.SetVolume(30), Assert.Single(_agent.Commands));
    }

    [Fact]
    public async Task UserChangeIsNotOverwrittenByTheNextPoll()
    {
        _player.Apply(Playing);

        _player.Position = 600;
        await _player.WhenSentAsync();
        _player.Apply(Playing);

        Assert.Equal(600, _player.Position);
    }

    [Fact]
    public async Task SelectingTracksSendsCommands()
    {
        _player.Apply(Playing);

        _player.SelectedAudioTrack = _player.AudioTracks[1];
        await _player.WhenSentAsync();
        _player.SelectedSubtitleTrack = _player.SubtitleTracks[1];
        await _player.WhenSentAsync();
        _player.SelectedSubtitleTrack = TrackOption.Off;
        await _player.WhenSentAsync();

        Assert.Equal(
            [PlayerCommand.SetAudioTrack(2), PlayerCommand.SetSubtitleTrack(3), PlayerCommand.SetSubtitleTrack(null)],
            _agent.Commands);
    }

    [Fact]
    public async Task PlayFileSendsTrimmedPathAndShowsErrors()
    {
        _agent.Result = AgentResult.Failure("File not found.");
        _player.FilePath = "  /mnt/movies/Dune.mkv ";

        await _player.PlayFileCommand.ExecuteAsync(null);

        Assert.Equal("/mnt/movies/Dune.mkv", _agent.PlayedPath);
        Assert.Equal("File not found.", _player.Error);
    }

    [Fact]
    public async Task SuccessfulCommandClearsError()
    {
        _agent.Result = AgentResult.Failure("The player is not running.");
        await _player.StopCommand.ExecuteAsync(null);
        Assert.NotNull(_player.Error);

        _agent.Result = AgentResult.Success;
        await _player.StopCommand.ExecuteAsync(null);

        Assert.Null(_player.Error);
    }
}
