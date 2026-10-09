using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.Tests;

public class LibraryViewModelTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private readonly FakeAgentClient _agent = new();
    private readonly LibraryViewModel _library;

    public LibraryViewModelTests()
    {
        _library = new LibraryViewModel(_agent, () => "http://pi.local:5080");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static LibraryItem Series(int? lastPlayed = null, params MediaFile[] files) => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Kind = MediaKind.Series,
        Title = "Shogun",
        Year = 2024,
        Files = files,
        LastPlayedFileId = lastPlayed,
        Downloads = [new LibraryDownload { Id = Guid.NewGuid(), Season = 1, Status = DownloadStatus.Ready, Progress = 1, SizeBytes = 20 * Gb }],
    };

    private static MediaFile Episode(int number, double position = 0, double duration = 0, bool watched = false) =>
        new(number, Guid.Empty, $"S01E{number:00}.mkv", 2 * Gb, 1, number, position, duration, watched);

    [Fact]
    public async Task RefreshShowsItemsAndStorage()
    {
        _agent.Library = [Series(null, Episode(1), Episode(2))];
        _agent.Storage = new StorageInfo(1000 * Gb, 400 * Gb);

        await _library.RefreshAsync(Token);

        var item = Assert.Single(_library.Items);
        Assert.Equal("Shogun", item.Title);
        Assert.Equal("2024 · Series · 4.0 GB", item.Subtitle);
        Assert.Equal("2 episodes · 0 watched", item.EpisodesSummary);
        Assert.Equal("400.0 GB free of 1000.0 GB", _library.StorageText);
        Assert.Equal(60, _library.StorageUsedPercent);
    }

    [Fact]
    public async Task RefreshReusesRowsAndDropsDeletedOnes()
    {
        _agent.Library = [Series(null, Episode(1))];
        await _library.RefreshAsync(Token);
        var row = _library.Items[0];

        _agent.Library = [Series(null, Episode(1), Episode(2))];
        await _library.RefreshAsync(Token);
        Assert.Same(row, _library.Items[0]);
        Assert.Equal(2, row.Episodes.Count);

        _agent.Library = [];
        await _library.RefreshAsync(Token);
        Assert.True(_library.IsEmpty);
    }

    [Fact]
    public async Task UnreachableAgentKeepsTheLastList()
    {
        _agent.Library = [Series(null, Episode(1))];
        await _library.RefreshAsync(Token);

        _agent.Library = null;
        await _library.RefreshAsync(Token);

        Assert.Single(_library.Items);
    }

    [Theory]
    [InlineData(null, 0, 0, false, "Watch S01E01")]
    [InlineData(1, 600, 3000, false, "Continue S01E01 from 10:00")]
    [InlineData(1, 0, 0, false, "Continue S01E01")]
    [InlineData(1, 2900, 3000, true, "Next S01E02")]
    public void PlayButtonSaysWhatWillPlay(int? lastPlayed, double position, double duration, bool watched, string expected)
    {
        var item = new LibraryItemViewModel(_library, Series(lastPlayed, Episode(1, position, duration, watched), Episode(2)));

        Assert.Equal(expected, item.PlayLabel);
    }

    [Fact]
    public void MovieButtonHasNoEpisodeCode()
    {
        var movie = new LibraryItem
        {
            Id = Guid.NewGuid(),
            Kind = MediaKind.Movie,
            Title = "Dune",
            Files = [new MediaFile(1, Guid.Empty, "Dune.mkv", 6 * Gb)],
        };

        Assert.Equal("Watch", new LibraryItemViewModel(_library, movie).PlayLabel);
    }

    [Fact]
    public async Task PlayContinuesAndShowsErrors()
    {
        _agent.Result = AgentResult.Failure("The player is not running.");
        var item = new LibraryItemViewModel(_library, Series(null, Episode(1)));

        await item.PlayCommand.ExecuteAsync(null);

        Assert.Equal(new PlayItemRequest(), _agent.PlayRequest);
        Assert.Equal("The player is not running.", _library.Error);
    }

    [Fact]
    public async Task EpisodeCanStartFromTheBeginning()
    {
        var item = new LibraryItemViewModel(_library, Series(null, Episode(1), Episode(2)));

        await item.Episodes[1].PlayFromStartCommand.ExecuteAsync(null);

        Assert.Equal(new PlayItemRequest(2, FromStart: true), _agent.PlayRequest);
    }

    [Theory]
    [InlineData(1, 600, true)]
    [InlineData(1, 0, false)]
    [InlineData(null, 0, false)]
    public void StartOverOnlyForAStoppedFile(int? lastPlayed, double position, bool expected)
    {
        var item = new LibraryItemViewModel(_library, Series(lastPlayed, Episode(1, position, 3000, false), Episode(2)));

        Assert.Equal(expected, item.CanStartOver);
    }

    [Fact]
    public async Task StartOverPlaysTheContinuedFileFromTheBeginning()
    {
        var item = new LibraryItemViewModel(_library, Series(1, Episode(1, 600, 3000, false), Episode(2)));

        await item.StartOverCommand.ExecuteAsync(null);

        Assert.Equal(new PlayItemRequest(1, FromStart: true), _agent.PlayRequest);
    }

    [Fact]
    public async Task DeleteNeedsConfirmation()
    {
        var item = new LibraryItemViewModel(_library, Series(null, Episode(1)));

        item.AskDeleteCommand.Execute(null);
        Assert.True(item.IsConfirmingDelete);
        Assert.Empty(_agent.Calls);

        await item.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal([$"delete {item.Id}"], _agent.Calls);
    }

    [Fact]
    public async Task SeasonDeleteNeedsConfirmation()
    {
        var item = new LibraryItemViewModel(_library, Series(null, Episode(1)));
        var season = Assert.Single(item.Downloads);
        Assert.True(season.CanDelete);
        Assert.Equal("Delete Season 1 and its files from the Pi?", season.DeleteQuestion);

        season.AskDeleteCommand.Execute(null);
        Assert.Empty(_agent.Calls);

        await season.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Single(_agent.Calls);
    }

    [Fact]
    public void MovieWithOneDownloadIsDeletedOnlyAsAWhole()
    {
        Guid[] downloads = [Guid.NewGuid(), Guid.NewGuid()];
        var movie = new LibraryItem
        {
            Id = Guid.NewGuid(),
            Kind = MediaKind.Movie,
            Title = "Dune",
            Downloads = [new LibraryDownload { Id = downloads[0], Status = DownloadStatus.Ready }],
        };

        var item = new LibraryItemViewModel(_library, movie);
        Assert.False(Assert.Single(item.Downloads).CanDelete);

        item.Update(movie with { Downloads = [.. movie.Downloads, new LibraryDownload { Id = downloads[1], Status = DownloadStatus.Error }] });
        Assert.All(item.Downloads, download => Assert.True(download.CanDelete));
    }

    [Fact]
    public async Task UntickedEpisodeIsSkippedWhileOtherFilesKeepTheirState()
    {
        var download = new LibraryDownload
        {
            Id = Guid.NewGuid(),
            Season = 1,
            Status = DownloadStatus.Downloading,
            Files =
            [
                new DownloadFile(0, "S01/E01.mkv", 2 * Gb, Wanted: true, IsVideo: true, Season: 1, Episode: 1),
                new DownloadFile(1, "S01/E02.mkv", 2 * Gb, Wanted: true, IsVideo: true, Season: 1, Episode: 2),
                new DownloadFile(2, "S01/E03.mkv", 2 * Gb, Wanted: true, IsVideo: true, Season: 1, Episode: 3),
                new DownloadFile(3, "S01/Subs/E01.srt", 1_000, Wanted: true),
                new DownloadFile(4, "S01/Sample/E01.mkv", 1_000, Wanted: false),
            ],
        };
        var item = new LibraryItemViewModel(_library, Series(null) with { Downloads = [download] });
        var row = Assert.Single(item.Downloads);

        Assert.True(row.CanChooseEpisodes);
        Assert.Equal(["S01E01", "S01E02", "S01E03"], row.Episodes.Select(episode => episode.Label));
        Assert.Equal("Episodes: 3 of 3", row.EpisodesChosen);

        row.Episodes[1].IsWanted = false;
        await row.SendSelectionAsync();

        Assert.Equal("Episodes: 2 of 3", row.EpisodesChosen);
        Assert.Contains($"select {download.Id} 0,2,3", _agent.Calls);
    }

    [Fact]
    public async Task EpisodeDeleteNeedsConfirmation()
    {
        var item = new LibraryItemViewModel(_library, Series(null, Episode(1), Episode(2)));
        var episode = item.Episodes[1];

        episode.AskDeleteCommand.Execute(null);
        Assert.Empty(_agent.Calls);

        await episode.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.Equal($"delete file {episode.File.Id}", _agent.Calls[0]);
    }

    [Fact]
    public void DownloadRowsDescribeProgress()
    {
        var download = new LibraryDownload
        {
            Id = Guid.NewGuid(),
            Season = 2,
            Episode = 1,
            Status = DownloadStatus.Downloading,
            Progress = 0.68,
            DownloadSpeed = 13_002_342,
            EtaSeconds = 250,
            SizeBytes = 10L * Gb,
        };

        var row = new DownloadViewModel(_library, Guid.NewGuid(), download);

        Assert.Equal("S02E01", row.Label);
        Assert.Equal("68% · 6.8 / 10.0 GB · 12.4 MB/s · 4 min left", row.StatusText);
        Assert.True(row.IsRunning);
    }

    [Fact]
    public async Task AddsSeasonFromTheForm()
    {
        _library.NewIsSeries = true;
        _library.NewTitle = " Shogun ";
        _library.NewSource = "magnet:?xt=urn:btih:abc";
        _library.NewSeason = "2";
        _library.IsAddFormOpen = true;

        await _library.AddCommand.ExecuteAsync(null);

        Assert.Equal(new AddToLibraryRequest(MediaKind.Series, "Shogun", "magnet:?xt=urn:btih:abc", Season: 2), _agent.Added);
        Assert.False(_library.IsAddFormOpen);
        Assert.Empty(_library.NewTitle);
    }

    [Theory]
    [InlineData("", "magnet:?x", "", "Title and magnet link are required.")]
    [InlineData("Shogun", "magnet:?x", "two", "Season and episode must be numbers.")]
    public async Task ValidatesTheForm(string title, string source, string season, string error)
    {
        _library.NewIsSeries = true;
        _library.NewTitle = title;
        _library.NewSource = source;
        _library.NewSeason = season;

        await _library.AddCommand.ExecuteAsync(null);

        Assert.Equal(error, _library.Error);
        Assert.Null(_agent.Added);
    }
}
