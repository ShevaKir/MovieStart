using Microsoft.Extensions.Logging.Abstractions;
using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class PlaybackWatchdogTests
{
    private const string Path = "/mnt/media/Dune.mkv";

    private readonly FakePlayer _player = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.Parse("2026-10-10T20:00:00Z"));
    private readonly PlaybackWatchdog _watchdog;

    public PlaybackWatchdogTests()
    {
        _watchdog = new PlaybackWatchdog(_player, _time, NullLogger<PlaybackWatchdog>.Instance);
    }

    private static PlayerState Playing(double position, bool paused = false) => new()
    {
        IsConnected = true,
        IsIdle = false,
        IsPaused = paused,
        FilePath = Path,
        Position = position,
        Duration = 7200,
        AudioTrackId = 2,
        SubtitleTrackId = 5,
    };

    private async Task CheckAfterAsync(TimeSpan elapsed)
    {
        _time.Advance(elapsed);
        await _watchdog.CheckAsync(TestContext.Current.CancellationToken);
    }

    private Task StallAsync() => CheckAfterAsync(PlaybackWatchdog.StallTimeout);

    [Fact]
    public async Task ReloadsStalledPlaybackAtCurrentPositionWithSameTracks()
    {
        _player.State = Playing(1000);
        await CheckAfterAsync(TimeSpan.Zero);

        await StallAsync();

        Assert.Equal(Path, _player.PlayedPath);
        Assert.Equal(1000 - PlaybackWatchdog.RewindSeconds, _player.PlayedStart);
        Assert.Equal(2, _player.PlayedOptions!.AudioTrackId);
        Assert.Equal(5, _player.PlayedOptions.Subtitles!.TrackId);
    }

    [Fact]
    public async Task LeavesMovingPlaybackAlone()
    {
        for (var position = 1000; position < 1030; position += 2)
        {
            _player.State = Playing(position);
            await CheckAfterAsync(TimeSpan.FromSeconds(2));
        }

        Assert.Null(_player.PlayedPath);
    }

    [Fact]
    public async Task LeavesPausedPlaybackAlone()
    {
        _player.State = Playing(1000, paused: true);
        await CheckAfterAsync(TimeSpan.Zero);

        await StallAsync();
        await StallAsync();

        Assert.Null(_player.PlayedPath);
    }

    [Fact]
    public async Task WaitsFullTimeoutAfterResume()
    {
        _player.State = Playing(1000, paused: true);
        await StallAsync();

        _player.State = Playing(1000);
        await CheckAfterAsync(TimeSpan.FromSeconds(2));

        Assert.Null(_player.PlayedPath);
    }

    [Fact]
    public async Task LeavesPlaybackAtTheEndAlone()
    {
        _player.State = Playing(7199);
        await CheckAfterAsync(TimeSpan.Zero);

        await StallAsync();

        Assert.Null(_player.PlayedPath);
    }

    [Fact]
    public async Task GivesUpAfterRepeatedRestartsWithoutProgress()
    {
        _player.State = Playing(1000);
        await CheckAfterAsync(TimeSpan.Zero);

        for (var i = 0; i < PlaybackWatchdog.MaxRestarts + 2; i++)
            await StallAsync();

        Assert.Equal(PlaybackWatchdog.MaxRestarts, _player.PlayCount);
    }

    [Fact]
    public async Task RestartsAgainAfterPlaybackRecovered()
    {
        _player.State = Playing(1000);
        await CheckAfterAsync(TimeSpan.Zero);
        for (var i = 0; i < PlaybackWatchdog.MaxRestarts; i++)
            await StallAsync();

        _player.State = Playing(1100);
        await CheckAfterAsync(TimeSpan.FromSeconds(2));

        await StallAsync();

        Assert.Equal(PlaybackWatchdog.MaxRestarts + 1, _player.PlayCount);
        Assert.Equal(1100 - PlaybackWatchdog.RewindSeconds, _player.PlayedStart);
    }
}
