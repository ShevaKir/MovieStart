using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Agent.Profile;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class LibraryServiceTests : IDisposable
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
    private const long Gb = 1024L * 1024 * 1024;

    private readonly string _root = Directory.CreateTempSubdirectory("library-").FullName;
    private readonly FakeQbitClient _qbit = new();
    private readonly FakePlayer _player = new();
    private readonly FakeStorage _storage = new() { Free = 100 * Gb };
    private readonly FakeProbe _probe = new();
    private readonly FakeProfiles _profiles = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FileLibraryStore _store;
    private readonly LibraryService _library;

    public LibraryServiceTests()
    {
        _store = new FileLibraryStore(Options.Create(new MediaOptions { Root = _root }), NullLogger<FileLibraryStore>.Instance);
        _library = CreateService();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LibraryService CreateService() => new(
        _store, _qbit, _player, _storage, _probe, _profiles, Options.Create(new MediaOptions { Root = _root }), _time,
        NullLogger<LibraryService>.Instance);

    private static AddToLibraryRequest Movie(string title = "Dune", long? size = 6 * Gb) =>
        new(MediaKind.Movie, title, Magnet, size, TmdbId: 438631, ReleaseTitle: "Dune.2021.1080p.BDRip.x265");

    private static AddToLibraryRequest Series(int? season = null, int? episode = null) =>
        new(MediaKind.Series, "Shogun", Magnet, 10 * Gb, TmdbId: 126308, Season: season, Episode: episode);

    [Fact]
    public async Task AddingStartsATaggedDownloadInTheItemFolder()
    {
        var item = await _library.AddAsync(Movie(), Token);

        var download = Assert.Single(item.Downloads);
        Assert.Equal(DownloadStatus.Queued, download.Status);
        var (source, savePath, tag) = Assert.Single(_qbit.Added);
        Assert.Equal(Magnet, source);
        Assert.Equal(_store.GetDownloadDirectory(item.Id, download.Id), savePath);
        Assert.Equal(LibraryService.TagFor(download.Id), tag);
        Assert.True(File.Exists(Path.Combine(_store.GetItemDirectory(item.Id), "item.json")));
    }

    [Fact]
    public async Task SeasonsAndEpisodesJoinTheSameSeries()
    {
        await _library.AddAsync(Series(season: 1), Token);
        await _library.AddAsync(Series(season: 2, episode: 1), Token);

        var series = Assert.Single(_library.List());
        Assert.Equal([(1, (int?)null), (2, 1)], series.Downloads.Select(d => (d.Season!.Value, d.Episode)));
    }

    [Fact]
    public async Task SeriesWithoutTmdbIdIsMatchedByTitle()
    {
        await _library.AddAsync(Series(season: 1) with { TmdbId = null }, Token);
        await _library.AddAsync(Series(season: 2) with { TmdbId = null, Title = "  shogun " }, Token);

        Assert.Equal(2, Assert.Single(_library.List()).Downloads.Count);
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/a.torrent")]
    public async Task RejectsUnsupportedSources(string source)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _library.AddAsync(Movie() with { Source = source }, Token));
        Assert.Empty(_qbit.Added);
    }

    [Fact]
    public async Task RejectsMovieWithEpisode()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _library.AddAsync(Movie() with { Episode = 1 }, Token));
    }

    [Fact]
    public async Task RefusesDownloadThatDoesNotFit()
    {
        _storage.Free = 7 * Gb;

        await Assert.ThrowsAsync<InsufficientStorageException>(() => _library.AddAsync(Movie(size: 6 * Gb), Token));

        Assert.Empty(_library.List());
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task CountsRemainingSizeOfRunningDownloads()
    {
        _storage.Free = 20 * Gb;
        await _library.AddAsync(Movie("First", size: 10 * Gb), Token);

        // 20 free − 10 still to download − 2 reserve = 8 available.
        await Assert.ThrowsAsync<InsufficientStorageException>(() => _library.AddAsync(Movie("Second", size: 9 * Gb), Token));
    }

    [Fact]
    public async Task FailedAddLeavesNoTrace()
    {
        _qbit.Unavailable = true;

        await Assert.ThrowsAsync<QbitUnavailableException>(() => _library.AddAsync(Movie(), Token));

        Assert.Empty(_library.List());
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task SyncTracksProgress()
    {
        var item = await _library.AddAsync(Movie(), Token);
        _qbit.Appear("h1", progress: 0.4, size: 5 * Gb);
        _qbit.Update("h1", t => t with { DownloadSpeed = 12_000_000, Eta = 240 });

        await _library.SyncAsync(Token);

        var download = Assert.Single(Assert.Single(_library.List()).Downloads);
        Assert.Equal(DownloadStatus.Downloading, download.Status);
        Assert.Equal(0.4, download.Progress);
        Assert.Equal(5 * Gb, download.SizeBytes);
        Assert.Equal(240, download.EtaSeconds);
        Assert.Equal("h1", download.TorrentHash);
        Assert.Equal(item.Id, _library.List()[0].Id);
    }

    [Theory]
    [InlineData("metaDL", DownloadStatus.Queued)]
    [InlineData("stalledDL", DownloadStatus.Downloading)]
    [InlineData("pausedDL", DownloadStatus.Paused)]
    [InlineData("stoppedDL", DownloadStatus.Paused)]
    [InlineData("error", DownloadStatus.Error)]
    public void MapsQbittorrentStates(string state, DownloadStatus expected)
    {
        Assert.Equal(expected, LibraryService.MapStatus(new QbitTorrent { State = state }));
    }

    [Fact]
    public async Task CompletedMovieKeepsOnlyTheMainVideoAndStopsSeeding()
    {
        var item = await _library.AddAsync(Movie(), Token);
        _qbit.Appear("h1", progress: 1, state: "uploading");
        _qbit.Files["h1"] =
        [
            new QbitFile { Name = "Dune/Dune.2021.mkv", Size = 6 * Gb },
            new QbitFile { Name = "Dune/Extras/Making of.mkv", Size = Gb },
            new QbitFile { Name = "Dune/Sample/sample.mkv", Size = 50_000_000 },
            new QbitFile { Name = "Dune/Dune.nfo", Size = 1_000 },
        ];

        await _library.SyncAsync(Token);

        var ready = Assert.Single(_library.List());
        Assert.Equal(DownloadStatus.Ready, Assert.Single(ready.Downloads).Status);
        var file = Assert.Single(ready.Files);
        Assert.Equal(Path.Combine(ready.Downloads[0].Id.ToString("N"), "Dune/Dune.2021.mkv"), file.Name);
        Assert.Equal(["h1"], _qbit.Paused);
    }

    [Fact]
    public async Task CompletedSeasonListsEpisodesInWatchOrder()
    {
        await _library.AddAsync(Series(season: 1), Token);
        _qbit.Appear("h1", progress: 1, state: "stalledUP");
        _qbit.Files["h1"] =
        [
            new QbitFile { Name = "Shogun.S01/Shogun.S01E02.mkv", Size = 2 * Gb },
            new QbitFile { Name = "Shogun.S01/Shogun.S01E10.mkv", Size = 2 * Gb },
            new QbitFile { Name = "Shogun.S01/Shogun.S01E01.mkv", Size = 2 * Gb },
            new QbitFile { Name = "Shogun.S01/Shogun.S01E01.sample.mkv", Size = 40_000_000 },
        ];

        await _library.SyncAsync(Token);

        var series = Assert.Single(_library.List());
        Assert.Equal(["S01E01", "S01E02", "S01E10"], series.Files.Select(f => f.EpisodeCode));
        Assert.Equal(3, series.Files.Select(f => f.Id).Distinct().Count());
    }

    [Fact]
    public async Task EpisodesFromLaterDownloadsAreMergedInOrder()
    {
        await _library.AddAsync(Series(season: 2, episode: 1), Token);
        _qbit.Appear("h2", progress: 1, state: "uploading");
        _qbit.Files["h2"] = [new QbitFile { Name = "Episode 1.mkv", Size = 2 * Gb }];
        await _library.SyncAsync(Token);

        await _library.AddAsync(Series(season: 1), Token);
        _qbit.Appear("h1", progress: 1, state: "uploading");
        _qbit.Files["h1"] = [new QbitFile { Name = "S01E01.mkv", Size = 2 * Gb }, new QbitFile { Name = "S01E02.mkv", Size = 2 * Gb }];
        await _library.SyncAsync(Token);

        var series = Assert.Single(_library.List());
        // The single-episode release gets its numbers from the request when the file name has none.
        Assert.Equal(["S01E01", "S01E02", "S02E01"], series.Files.Select(f => f.EpisodeCode));
        Assert.Equal(1, series.Files.Single(f => f.EpisodeCode == "S02E01").Id);
    }

    [Fact]
    public async Task MissingTorrentBecomesAnErrorAfterGracePeriod()
    {
        await _library.AddAsync(Movie(), Token);

        await _library.SyncAsync(Token);
        Assert.Equal(DownloadStatus.Queued, _library.List()[0].Downloads[0].Status);

        _time.Advance(TimeSpan.FromMinutes(2));
        await _library.SyncAsync(Token);
        Assert.Equal(DownloadStatus.Error, _library.List()[0].Downloads[0].Status);
    }

    [Fact]
    public async Task LibrarySurvivesRestart()
    {
        await AddReadySeasonAsync();

        var restarted = CreateService();
        await restarted.LoadAsync(Token);

        var series = Assert.Single(restarted.List());
        Assert.Equal(3, series.Files.Count);
        Assert.Equal(DownloadStatus.Ready, series.Downloads[0].Status);
    }

    [Fact]
    public async Task PlayingBeforeAnythingIsDownloadedFails()
    {
        var item = await _library.AddAsync(Movie(), Token);

        await Assert.ThrowsAsync<LibraryStateException>(() => _library.PlayAsync(item.Id, new PlayItemRequest(), Token));
    }

    [Fact]
    public async Task ContinueResumesSlightlyBeforeTheSavedPosition()
    {
        var series = await AddReadySeasonAsync();
        var episode2 = series.Files[1];
        await _library.RecordProgressAsync(PathOf(series, episode2), 600, 3000, Token);

        var played = await _library.PlayAsync(series.Id, new PlayItemRequest(), Token);

        Assert.Equal(episode2.Id, played.Id);
        Assert.Equal(PathOf(series, episode2), _player.PlayedPath);
        Assert.Equal(595, _player.PlayedStart);
    }

    [Fact]
    public async Task ContinueMovesToNextEpisodeOnceFinished()
    {
        var series = await AddReadySeasonAsync();
        await _library.RecordProgressAsync(PathOf(series, series.Files[0]), 2900, 3000, Token);

        var played = await _library.PlayAsync(series.Id, new PlayItemRequest(), Token);

        Assert.Equal(series.Files[1].Id, played.Id);
        Assert.Equal(0, _player.PlayedStart);
        Assert.True(_library.List()[0].Files[0].Watched);
    }

    [Fact]
    public async Task ExplicitFileFromStartIgnoresPosition()
    {
        var series = await AddReadySeasonAsync();
        await _library.RecordProgressAsync(PathOf(series, series.Files[2]), 1200, 3000, Token);

        await _library.PlayAsync(series.Id, new PlayItemRequest(series.Files[2].Id, FromStart: true), Token);

        Assert.Equal(0, _player.PlayedStart);
    }

    [Fact]
    public async Task UnknownFileIsNotFound()
    {
        var series = await AddReadySeasonAsync();

        await Assert.ThrowsAsync<LibraryNotFoundException>(() => _library.PlayAsync(series.Id, new PlayItemRequest(99), Token));
    }

    [Fact]
    public async Task ProgressIsPersisted()
    {
        var series = await AddReadySeasonAsync();
        await _library.RecordProgressAsync(PathOf(series, series.Files[0]), 120, 3000, Token);

        var restarted = CreateService();
        await restarted.LoadAsync(Token);

        var reloaded = restarted.List()[0];
        Assert.Equal(series.Files[0].Id, reloaded.LastPlayedFileId);
        Assert.Equal(120, reloaded.Files[0].Position);
    }

    [Fact]
    public async Task ProgressForUnknownFilesIsIgnored()
    {
        await AddReadySeasonAsync();

        await _library.RecordProgressAsync("/somewhere/else.mkv", 100, 1000, Token);

        Assert.Null(_library.List()[0].LastPlayedFileId);
    }

    [Fact]
    public async Task DeletingADownloadRemovesItsEpisodes()
    {
        await AddReadySeasonAsync();
        await _library.AddAsync(Series(season: 2), Token);
        _qbit.Appear("h2", progress: 0.5);
        await _library.SyncAsync(Token);
        var series = _library.List()[0];
        var season1 = series.Downloads[0];

        await _library.DeleteDownloadAsync(series.Id, season1.Id, Token);

        var remaining = Assert.Single(_library.List());
        Assert.Empty(remaining.Files);
        Assert.Single(remaining.Downloads);
        Assert.Contains(("h1", true), _qbit.Deleted);
        Assert.False(Directory.Exists(_store.GetDownloadDirectory(series.Id, season1.Id)));
    }

    [Fact]
    public async Task DeletingTheLastDownloadDeletesTheItem()
    {
        var series = await AddReadySeasonAsync();

        await _library.DeleteDownloadAsync(series.Id, series.Downloads[0].Id, Token);

        Assert.Empty(_library.List());
        Assert.False(Directory.Exists(_store.GetItemDirectory(series.Id)));
    }

    [Fact]
    public async Task DeletingStopsPlaybackOfThatItem()
    {
        var series = await AddReadySeasonAsync();
        _player.State = new PlayerState { IsConnected = true, IsIdle = false, FilePath = PathOf(series, series.Files[0]) };

        await _library.DeleteAsync(series.Id, Token);

        Assert.Equal(PlayerCommand.Stop(), _player.LastCommand);
    }

    [Fact]
    public async Task DeletingAQueuedDownloadFindsTheTorrentByTag()
    {
        var item = await _library.AddAsync(Movie(), Token);
        _qbit.Appear("h1", state: "metaDL");

        await _library.DeleteAsync(item.Id, Token);

        Assert.Equal([("h1", true)], _qbit.Deleted);
    }

    [Fact]
    public async Task PausingRequiresARunningDownload()
    {
        var series = await AddReadySeasonAsync();

        await Assert.ThrowsAsync<LibraryStateException>(
            () => _library.PauseDownloadAsync(series.Id, series.Downloads[0].Id, Token));
    }

    [Fact]
    public async Task ProbingStoresTracksAndDuration()
    {
        var series = await AddReadySeasonAsync();
        _probe.Result = new ProbeResult(3480, [new MediaTrack(1, "ukr", "Дубляж"), new MediaTrack(2, "eng", null)], [new MediaTrack(1, "ukr", "Forced")]);

        Assert.Equal(3, await _library.ProbeNewFilesAsync(Token));
        Assert.Equal(0, await _library.ProbeNewFilesAsync(Token));

        var file = _library.List()[0].Files[0];
        Assert.Equal(2, file.AudioTracks!.Count);
        Assert.Single(file.SubtitleTracks!);
        Assert.Equal(3480, file.Duration);
        Assert.Equal(PathOf(series, series.Files[0]), _probe.Probed[0]);
    }

    [Fact]
    public async Task UnreadableFilesAreNotProbedAgain()
    {
        await AddReadySeasonAsync();
        _probe.Result = null;

        await _library.ProbeNewFilesAsync(Token);

        Assert.All(_library.List()[0].Files, file => Assert.Empty(file.AudioTracks!));
        Assert.Equal(0, await _library.ProbeNewFilesAsync(Token));
    }

    [Fact]
    public async Task PlaybackSelectsTracksFromTheProfile()
    {
        var series = await AddReadySeasonAsync();
        _probe.Result = new ProbeResult(
            3000,
            [new MediaTrack(1, "rus", "MVO, LostFilm"), new MediaTrack(2, "ukr", "Дубляж"), new MediaTrack(3, "eng", "Original")],
            [new MediaTrack(1, "ukr", "Написи"), new MediaTrack(2, "eng", null)]);
        await _library.ProbeNewFilesAsync(Token);

        await _library.PlayAsync(series.Id, new PlayItemRequest(), Token);

        Assert.Equal(new PlaybackOptions(0, 2, new SubtitleChoice(1)), _player.PlayedOptions);
    }

    [Fact]
    public async Task UnprobedFilesLeaveTrackChoiceToThePlayer()
    {
        var series = await AddReadySeasonAsync();

        await _library.PlayAsync(series.Id, new PlayItemRequest(), Token);

        Assert.Equal(new PlaybackOptions(), _player.PlayedOptions);
    }

    [Fact]
    public async Task FileListArrivesWithMetadataAndSkipsSamples()
    {
        await _library.AddAsync(Series(season: 1), Token);
        _qbit.Appear("h1", state: "metaDL");
        _qbit.Files["h1"] = SeasonFiles();

        await _library.SyncAsync(Token);
        Assert.Null(_library.List()[0].Downloads[0].Files);

        _qbit.Update("h1", torrent => torrent with { State = "downloading", Progress = 0.1 });
        await _library.SyncAsync(Token);

        var files = _library.List()[0].Downloads[0].Files!;
        Assert.Equal(["S01E01", "S01E02", "S01E03", null], files.Select(file => file.EpisodeCode));
        Assert.Equal([true, true, true, false], files.Select(file => file.Wanted));
        Assert.Equal(["h1:3=0"], _qbit.PriorityChanges);
    }

    [Fact]
    public async Task ChosenEpisodesAreTheOnlyOnesInTheLibrary()
    {
        var series = await AddRunningSeasonAsync();
        var download = series.Downloads[0];

        await _library.SelectFilesAsync(series.Id, download.Id, new SelectFilesRequest([0, 2]), Token);

        Assert.Equal([true, false, true, false], _library.List()[0].Downloads[0].Files!.Select(file => file.Wanted));
        Assert.Equal([true, false, true, false], _qbit.Files["h1"].Select(file => file.IsWanted));

        _qbit.Update("h1", torrent => torrent with { State = "uploading", Progress = 1 });
        await _library.SyncAsync(Token);

        Assert.Equal(["S01E01", "S01E03"], _library.List()[0].Files.Select(file => file.EpisodeCode));
    }

    [Fact]
    public async Task DroppedEpisodeIsFreedAndARestoredOneIsRechecked()
    {
        var series = await AddRunningSeasonAsync();
        var download = series.Downloads[0];
        var episode2 = Path.Combine(_store.GetDownloadDirectory(series.Id, download.Id), "Shogun.S01/Shogun.S01E02.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(episode2)!);
        await File.WriteAllTextAsync(episode2, "partial", Token);

        await _library.SelectFilesAsync(series.Id, download.Id, new SelectFilesRequest([0, 2]), Token);

        Assert.False(File.Exists(episode2));
        Assert.Empty(_qbit.Rechecked);

        await _library.SelectFilesAsync(series.Id, download.Id, new SelectFilesRequest([0, 1, 2]), Token);

        Assert.Equal(["h1"], _qbit.Rechecked);
    }

    [Fact]
    public async Task RecheckIsNotMistakenForAFinishedDownload()
    {
        var series = await AddRunningSeasonAsync();
        _qbit.Update("h1", torrent => torrent with { State = "checkingDL", Progress = 1 });

        await _library.SyncAsync(Token);

        var download = _library.List()[0].Downloads[0];
        Assert.Equal(DownloadStatus.Downloading, download.Status);
        Assert.Equal(0.1, download.Progress);
        Assert.Empty(_library.List()[0].Files);
        Assert.Empty(_qbit.Paused);
    }

    [Fact]
    public async Task EpisodeWithoutSeasonInItsNameTakesTheDownloadSeason()
    {
        await _library.AddAsync(Series(season: 2), Token);
        _qbit.Appear("h1", progress: 0.1);
        _qbit.Files["h1"] = [new QbitFile { Name = "Show/01. Inferno.mkv", Size = Gb }, new QbitFile { Name = "Show/02. Cover-Up.mkv", Size = Gb }];

        await _library.SyncAsync(Token);

        Assert.Equal(["S02E01", "S02E02"], _library.List()[0].Downloads[0].Files!.Select(file => file.EpisodeCode));
    }

    [Fact]
    public async Task ChoosingNeedsAVideoFile()
    {
        var series = await AddRunningSeasonAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _library.SelectFilesAsync(series.Id, series.Downloads[0].Id, new SelectFilesRequest([]), Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _library.SelectFilesAsync(series.Id, series.Downloads[0].Id, new SelectFilesRequest([0, 9]), Token));
    }

    [Fact]
    public async Task ChoosingFilesOfAFinishedDownloadIsRefused()
    {
        var series = await AddReadySeasonAsync();

        await Assert.ThrowsAsync<LibraryStateException>(
            () => _library.SelectFilesAsync(series.Id, series.Downloads[0].Id, new SelectFilesRequest([0]), Token));
    }

    [Fact]
    public async Task DeletedEpisodeIsSkippedSoItIsNotFetchedAgain()
    {
        var series = await AddReadySeasonAsync();
        var episode = series.Files[1];
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(series, episode))!);
        await File.WriteAllTextAsync(PathOf(series, episode), "video", Token);

        await _library.DeleteFileAsync(series.Id, episode.Id, Token);

        var after = Assert.Single(_library.List());
        Assert.Equal(["S01E01", "S01E03"], after.Files.Select(file => file.EpisodeCode));
        Assert.Single(after.Downloads);
        Assert.False(File.Exists(PathOf(series, episode)));
        Assert.Equal(["h1:1=0"], _qbit.PriorityChanges);
        Assert.Empty(_qbit.Deleted);
    }

    [Fact]
    public async Task DeletingThePlayingEpisodeStopsItAndForgetsIt()
    {
        var series = await AddReadySeasonAsync();
        await _library.PlayAsync(series.Id, new PlayItemRequest(series.Files[0].Id), Token);
        _player.State = new PlayerState { IsConnected = true, IsIdle = false, FilePath = PathOf(series, series.Files[0]) };

        await _library.DeleteFileAsync(series.Id, series.Files[0].Id, Token);

        Assert.Equal(PlayerCommand.Stop(), _player.LastCommand);
        Assert.Null(_library.List()[0].LastPlayedFileId);
    }

    [Fact]
    public async Task DeletingTheLastEpisodeTakesTheDownloadWithIt()
    {
        var series = await AddReadySeasonAsync();

        foreach (var file in series.Files)
            await _library.DeleteFileAsync(series.Id, file.Id, Token);

        Assert.Empty(_library.List());
        Assert.Equal([("h1", true)], _qbit.Deleted);
    }

    private static List<QbitFile> SeasonFiles() =>
    [
        new QbitFile { Name = "Shogun.S01/Shogun.S01E01.mkv", Size = 2 * Gb },
        new QbitFile { Name = "Shogun.S01/Shogun.S01E02.mkv", Size = 2 * Gb },
        new QbitFile { Name = "Shogun.S01/Shogun.S01E03.mkv", Size = 2 * Gb },
        new QbitFile { Name = "Shogun.S01/Sample/Shogun.S01E01.mkv", Size = 40_000_000 },
    ];

    private async Task<LibraryItem> AddRunningSeasonAsync()
    {
        await _library.AddAsync(Series(season: 1), Token);
        _qbit.Appear("h1", progress: 0.1);
        _qbit.Files["h1"] = SeasonFiles();
        await _library.SyncAsync(Token);
        return _library.List().Single();
    }

    private async Task<LibraryItem> AddReadySeasonAsync()
    {
        await _library.AddAsync(Series(season: 1), Token);
        _qbit.Appear("h1", progress: 1, state: "uploading");
        _qbit.Files["h1"] = [.. Enumerable.Range(1, 3).Select(e => new QbitFile { Name = $"S01E0{e}.mkv", Size = 2 * Gb })];
        await _library.SyncAsync(Token);
        return _library.List().Single();
    }

    private string PathOf(LibraryItem item, MediaFile file) => Path.Combine(_store.GetItemDirectory(item.Id), file.Name);

    private sealed class FakeStorage : IStorageProbe
    {
        public long Free { get; set; }

        public StorageInfo Measure(string path) => new(500 * Gb, Free);
    }

    private sealed class FakeProbe : IMediaProbe
    {
        public ProbeResult? Result { get; set; }

        public List<string> Probed { get; } = [];

        public Task<ProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            Probed.Add(path);
            return Task.FromResult(Result);
        }
    }
}
