using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Demo;
using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class DemoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("demo-").FullName;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OriginalTitleGetsTrackerReleasesAndLocalizedTitleGetsToloka()
    {
        var prowlarr = new DemoProwlarrClient();

        var original = await prowlarr.SearchAsync("Dune 2021", 2000, Token);
        var localized = await prowlarr.SearchAsync("Дюна 2021", 2000, Token);

        Assert.DoesNotContain(original, release => release.Indexer.StartsWith("Toloka"));
        Assert.All(localized, release => Assert.StartsWith("Toloka", release.Indexer));
        Assert.All(original, release => Assert.Contains("2021", release.Title));
    }

    [Fact]
    public async Task SeriesReleasesCoverSeasonsAndEpisodes()
    {
        var releases = await new DemoProwlarrClient().SearchAsync("Shogun", 5000, Token);

        Assert.Contains(releases, release => release.Title.Contains("Сезон: 2"));
        Assert.Contains(releases, release => release.Title.Contains("S02E01"));
    }

    [Fact]
    public async Task DownloadProgressesWithTimeAndPausing()
    {
        var qbit = CreateQbit();
        await qbit.AddAsync(Magnet("Dune / Dune [2021, BDRip 1080p] Dub"), _root, "ms-1", Token);

        Assert.Equal("metaDL", (await qbit.GetTorrentsAsync(Token))[0].State);

        _time.Advance(TimeSpan.FromSeconds(2 + 5));
        var running = (await qbit.GetTorrentsAsync(Token))[0];
        Assert.Equal("downloading", running.State);
        Assert.Equal(0.5, running.Progress, 3);
        Assert.Equal("ms-1", running.Tags);

        await qbit.PauseAsync(running.Hash, Token);
        _time.Advance(TimeSpan.FromSeconds(30));
        var paused = (await qbit.GetTorrentsAsync(Token))[0];
        Assert.Equal(("pausedDL", 0.5), (paused.State, Math.Round(paused.Progress, 3)));
    }

    [Fact]
    public async Task FinishedDownloadWritesItsFiles()
    {
        var qbit = CreateQbit();
        await qbit.AddAsync(Magnet("Show / Show / Сезон: 1 / Серии: 1-3 из 3 [2024, WEB-DL 1080p] MVO"), _root, "ms-1", Token);
        qbit.CompleteAll();

        QbitTorrentState? state = null;
        for (var i = 0; i < 100 && state is not { Progress: 1 }; i++)
        {
            var torrent = (await qbit.GetTorrentsAsync(Token))[0];
            state = new QbitTorrentState(torrent.Progress, torrent.Hash);
            await Task.Delay(20, Token);
        }

        var files = await qbit.GetFilesAsync(state!.Hash, Token);
        Assert.Equal(3, files.Count);
        Assert.All(files, file => Assert.True(File.Exists(Path.Combine(_root, file.Name))));

        await qbit.DeleteAsync(state.Hash, deleteFiles: true, Token);
        Assert.False(Directory.Exists(_root));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task PlayerFollowsCommandsAndTheClock()
    {
        var player = new DemoPlayer(new FixedProbe(), _time);

        await player.PlayAsync("/movies/a.mkv", new PlaybackOptions(60, 2, new SubtitleChoice(null)), Token);
        Assert.Equal((false, 60d, 2, (int?)null), (player.State.IsIdle, player.State.Position, player.State.AudioTrackId!.Value, player.State.SubtitleTrackId));

        await player.SendAsync(PlayerCommand.SeekRelative(30), Token);
        await player.SendAsync(PlayerCommand.SetSubtitleTrack(1), Token);
        await player.SendAsync(PlayerCommand.Pause(), Token);
        Assert.Equal((90d, 1, true), (player.State.Position, player.State.SubtitleTrackId!.Value, player.State.IsPaused));

        await Assert.ThrowsAsync<PlayerCommandException>(() => player.SendAsync(PlayerCommand.SetAudioTrack(9), Token));

        await player.SendAsync(PlayerCommand.Stop(), Token);
        Assert.True(player.State.IsIdle);
        await Assert.ThrowsAsync<PlayerCommandException>(() => player.SendAsync(PlayerCommand.Pause(), Token));
    }

    private DemoQbitClient CreateQbit()
    {
        // A missing ffmpeg makes empty files; the flow is the same.
        var options = Options.Create(new DemoOptions { DownloadSeconds = 10, FfmpegPath = "/nonexistent/ffmpeg" });
        return new DemoQbitClient(
            new DemoClipFactory(options, NullLogger<DemoClipFactory>.Instance), options, _time, NullLogger<DemoQbitClient>.Instance);
    }

    private static string Magnet(string title) =>
        $"magnet:?xt=urn:btih:{Guid.NewGuid():N}&dn={Uri.EscapeDataString(title)}&xl={6L * 1024 * 1024 * 1024}";

    private sealed record QbitTorrentState(double Progress, string Hash);

    private sealed class FixedProbe : IMediaProbe
    {
        public Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<ProbeResult?>(new ProbeResult(
                600,
                [new MediaTrack(1, "ukr", "Дубляж"), new MediaTrack(2, "eng", "Original")],
                [new MediaTrack(1, "ukr", "Forced")]));
    }
}
