using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Library;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.Tests;

public class SearchViewModelTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static readonly TitleResult Shogun = new(MediaKind.Series, 126308, "Сьоґун", "Shōgun", 2024, "Overview", null, 8.6);
    private static readonly TitleResult Dune = new(MediaKind.Movie, 438631, "Dune", "Dune", 2021, null, null, 7.8);

    private readonly FakeAgentClient _agent = new();
    private readonly LibraryViewModel _library;
    private readonly SearchViewModel _search;

    public SearchViewModelTests()
    {
        _library = new LibraryViewModel(_agent, () => "http://pi.local:5080");
        _search = new SearchViewModel(_agent, () => "http://pi.local:5080", _library, new NoPosters());
    }

    private static ReleaseInfo Release(string title, int? season = null, params ReleaseAudio[] audio) => new()
    {
        Title = title,
        Indexer = "RuTracker",
        DownloadSource = $"magnet:?{title}",
        SizeBytes = 6 * Gb,
        Seeders = 10,
        Resolution = 1080,
        Source = ReleaseSource.BdRip,
        Codec = "x265",
        Is10Bit = true,
        Container = "MKV",
        SeasonFrom = season,
        Audio = audio,
    };

    [Fact]
    public async Task ShowsTitlesAndMarksWhatIsInTheLibrary()
    {
        _agent.Library = [new LibraryItem { Id = Guid.NewGuid(), Kind = MediaKind.Movie, TmdbId = 438631, Title = "Dune" }];
        await _library.RefreshAsync(TestContext.Current.CancellationToken);
        _agent.Titles = [Shogun, Dune];
        _search.Query = "дюна";

        await _search.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["Сьоґун", "Dune"], _search.Results.Select(r => r.Name));
        Assert.Equal([false, true], _search.Results.Select(r => r.IsInLibrary));
        Assert.Equal("2024 · Series · Shōgun", _search.Results[0].Details);
        Assert.Equal("8.6", _search.Results[0].Rating);
    }

    [Fact]
    public async Task ExplainsEmptyResults()
    {
        _search.Query = "zzzz";

        await _search.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Nothing found. Try the original title.", _search.Error);
    }

    [Fact]
    public async Task OpeningATitleLoadsReleasesWithTheTypedQuery()
    {
        _agent.Titles = [Dune];
        _agent.Releases = [Release("A"), Release("B")];
        _search.Query = "дюна";
        await _search.SearchCommand.ExecuteAsync(null);

        await _search.Results[0].OpenCommand.ExecuteAsync(null);

        Assert.False(_search.IsShowingResults);
        Assert.Equal("дюна", _agent.ReleaseQuery);
        Assert.Equal(2, _search.Selected!.Releases.Count);
        Assert.True(_search.Selected.Releases[0].IsBest);

        _search.BackCommand.Execute(null);
        Assert.True(_search.IsShowingResults);
    }

    [Fact]
    public async Task LanguageAndVoiceFiltersCombinePerTrack()
    {
        _agent.Releases =
        [
            Release("rus dub + ukr mvo", null, new ReleaseAudio("rus", VoiceType.Dub), new ReleaseAudio("ukr", VoiceType.Mvo)),
            Release("ukr dub", null, new ReleaseAudio("ukr", VoiceType.Dub)),
            Release("eng only", null, new ReleaseAudio("eng", VoiceType.Original)),
        ];
        var releases = await OpenAsync(Dune);

        releases.Languages.Single(c => c.Value == "ukr").IsSelected = true;
        Assert.Equal(["rus dub + ukr mvo", "ukr dub"], releases.Releases.Select(r => r.Release.Title));

        // Ukrainian dub, not "Ukrainian anything and dub in any language".
        releases.Voices.Single(c => c.Value == VoiceType.Dub).IsSelected = true;
        Assert.Equal(["ukr dub"], releases.Releases.Select(r => r.Release.Title));
        Assert.True(releases.Releases[0].IsBest);
        Assert.Equal("1 of 3", releases.Summary);
    }

    [Fact]
    public async Task MatchingTracksAreHighlighted()
    {
        _agent.Releases = [Release("mixed", null, new ReleaseAudio("rus", VoiceType.Mvo, "HDRezka"), new ReleaseAudio("ukr", VoiceType.Dub))];
        var releases = await OpenAsync(Dune);

        releases.Languages.Single(c => c.Value == "ukr").IsSelected = true;

        Assert.Equal(
            [new AudioChip("RUS MVO HDRezka", false), new AudioChip("UKR Dub", true)],
            releases.Releases[0].Audio);
    }

    [Fact]
    public async Task SeasonChipsComeFromTheReleases()
    {
        _agent.Releases =
        [
            Release("S1", 1),
            Release("S2", 2),
            Release("S1-S3", 1) with { SeasonTo = 3 },
            Release("Complete"),
        ];
        var releases = await OpenAsync(Shogun);

        Assert.Equal(["Season 1", "Season 2"], releases.Seasons.Select(c => c.Label));

        releases.Seasons.Single(c => c.Value == 2).IsSelected = true;

        Assert.Equal(["S2", "S1-S3"], releases.Releases.Select(r => r.Release.Title));
    }

    [Fact]
    public void RowsDescribeQualityAndEpisodes()
    {
        var row = new ReleaseRowViewModel(null!, Release("x", 1) with { EpisodeFrom = 1, EpisodeTo = 10, Subtitles = ["rus", "eng"] });

        Assert.Equal("BDRip · x265 10-bit · MKV · RuTracker", row.Quality);
        Assert.Equal("Season 1 · episodes 1–10", row.Episodes);
        Assert.Equal("Sub RUS, ENG", row.Subtitles);
        Assert.Equal("6.0 GB", row.Size);
    }

    [Fact]
    public async Task DownloadAddsToTheLibraryOnce()
    {
        _agent.Releases = [Release("Shogun.S02E03", 2) with { EpisodeFrom = 3 }];
        var releases = await OpenAsync(Shogun);
        var row = releases.Releases[0];

        await row.DownloadCommand.ExecuteAsync(null);
        await row.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(["add"], _agent.Calls);
        Assert.Equal((MediaKind.Series, "Сьоґун", 2, 3), (_agent.Added!.Kind, _agent.Added.Title, _agent.Added.Season!.Value, _agent.Added.Episode!.Value));
        Assert.True(row.IsAdded);
        Assert.Equal("Added", row.DownloadLabel);
        Assert.True(releases.Title.IsInLibrary);
    }

    [Fact]
    public async Task ShowsAgentErrors()
    {
        _agent.Result = AgentResult.Failure("Prowlarr is not configured: set Prowlarr:ApiKey.");

        var releases = await OpenAsync(Dune);

        Assert.Equal("Prowlarr is not configured: set Prowlarr:ApiKey.", releases.Error);
        Assert.Empty(releases.Releases);
    }

    private async Task<ReleasesViewModel> OpenAsync(TitleResult title)
    {
        var result = new TitleResultViewModel(_search, title, new NoPosters());
        await _search.OpenAsync(result);
        return _search.Selected!;
    }
}
