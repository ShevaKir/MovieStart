using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Agent.Media;
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
        _store, _qbit, _player, _storage, Options.Create(new MediaOptions { Root = _root }), _time, NullLogger<LibraryService>.Instance);

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
}
