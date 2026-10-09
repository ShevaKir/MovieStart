using MovieStart.Agent.Profile;
using MovieStart.Agent.Search;
using MovieStart.Shared.Library;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Tests;

public class SearchServiceTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private readonly FakeProwlarr _prowlarr = new();
    private readonly FakeTmdb _tmdb = new();
    private readonly FakeProfiles _profiles = new();
    private readonly SearchService _search;

    public SearchServiceTests()
    {
        _search = new SearchService(_tmdb, _prowlarr, _profiles);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ReleaseSearch Dune => new(MediaKind.Movie, "Дюна", "Dune", 2021, "дюна");

    [Theory]
    [InlineData("Дюна", "ru-RU")]
    [InlineData("Сьоґун", "uk-UA")]
    [InlineData("Їжак", "uk-UA")]
    [InlineData("Dune", "en-US")]
    public async Task TitleSearchUsesTheLanguageOfTheQuery(string query, string expected)
    {
        await _search.SearchTitlesAsync(query, Token);

        Assert.Equal(expected, _tmdb.Language);
    }

    [Fact]
    public void MovieQueriesIncludeTheYearAndSkipDuplicates()
    {
        Assert.Equal(["Dune 2021", "Дюна 2021"], SearchService.BuildQueries(Dune));
    }

    [Fact]
    public void SeriesQueriesHaveNoYear()
    {
        Assert.Equal(["Shōgun", "Сёгун"], SearchService.BuildQueries(new ReleaseSearch(MediaKind.Series, "Сёгун", "Shōgun", 2024)));
    }

    [Fact]
    public async Task KeepsOnly1080pWithSeedersAndNoCamRips()
    {
        _prowlarr.Add("Dune.2021.1080p.BluRay.x265", seeders: 10);
        _prowlarr.Add("Dune.2021.2160p.BluRay.x265", seeders: 10);
        _prowlarr.Add("Dune.2021.720p.BluRay.x264", seeders: 10);
        _prowlarr.Add("Dune 2021 CAMRip 1080p", seeders: 10);
        _prowlarr.Add("Dune.2021.1080p.WEB-DL", seeders: 0);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Equal(["Dune.2021.1080p.BluRay.x265"], releases.Select(r => r.Title));
    }

    [Fact]
    public async Task DropsOtherYearsAndUnrelatedTitles()
    {
        _prowlarr.Add("Dune.1984.1080p.BluRay", seeders: 5);
        _prowlarr.Add("Dune.Part.Two.2024.1080p.WEB-DL", seeders: 5);
        _prowlarr.Add("Arrival.2016.1080p.BluRay", seeders: 5);
        _prowlarr.Add("Дюна / Dune [2021, BDRip 1080p] Dub", seeders: 5);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Equal(["Дюна / Dune [2021, BDRip 1080p] Dub"], releases.Select(r => r.Title));
    }

    [Fact]
    public async Task MoviesDoNotGetSeasonPacks()
    {
        _prowlarr.Add("Dune.2021.1080p.BluRay", seeders: 5);
        _prowlarr.Add("Dune.S01.2021.1080p.WEB-DL", seeders: 5);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Single(releases);
    }

    [Fact]
    public async Task MatchesTitlesIgnoringDiacritics()
    {
        _prowlarr.Add("Сёгун / Shogun / Сезон: 1 / Серии: 1-10 из 10 [2024, WEB-DL 1080p] MVO", seeders: 5);

        var releases = await _search.SearchReleasesAsync(new ReleaseSearch(MediaKind.Series, "Сёгун", "Shōgun", 2024), Token);

        var release = Assert.Single(releases);
        Assert.Equal(1, release.Season);
        Assert.Null(release.Episode);
    }

    [Fact]
    public async Task RanksBetterSourcesAndFormatsFirst()
    {
        _prowlarr.Add("Dune.2021.1080p.WEBRip.x264", seeders: 900);
        _prowlarr.Add("Dune.2021.1080p.BluRay.x264.mp4", seeders: 50, size: 12 * Gb);
        _prowlarr.Add("Dune.2021.1080p.BluRay.x265.10bit.mkv", seeders: 20, size: 7 * Gb);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Equal(
            ["Dune.2021.1080p.BluRay.x265.10bit.mkv", "Dune.2021.1080p.BluRay.x264.mp4", "Dune.2021.1080p.WEBRip.x264"],
            releases.Select(r => r.Title));
    }

    [Fact]
    public async Task ReleasesWithThePreferredVoiceComeFirst()
    {
        _prowlarr.Add("Dune.2021.1080p.BluRay.x265.10bit.mkv", seeders: 500);
        _prowlarr.Add("Дюна / Dune [2021, WEB-DL 1080p] MVO (HDRezka Studio)", seeders: 5);
        _prowlarr.Add("Дюна / Dune [2021, WEBRip 1080p] Ukr (Dub)", seeders: 5);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        // Default profile: Ukrainian dub, Russian dub, Russian multi-voice, English original.
        Assert.Equal(
            ["Дюна / Dune [2021, WEBRip 1080p] Ukr (Dub)", "Дюна / Dune [2021, WEB-DL 1080p] MVO (HDRezka Studio)", "Dune.2021.1080p.BluRay.x265.10bit.mkv"],
            releases.Select(r => r.Title));
    }

    [Fact]
    public async Task CustomProfileChangesTheOrder()
    {
        _profiles.Profile = new VoiceProfile([new VoicePreference("rus", VoiceType.Mvo, "HDRezka")], []);
        _prowlarr.Add("Дюна / Dune [2021, BDRip 1080p] Ukr (Dub)", seeders: 50);
        _prowlarr.Add("Дюна / Dune [2021, WEBRip 1080p] MVO (HDRezka Studio)", seeders: 5);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Equal("Дюна / Dune [2021, WEBRip 1080p] MVO (HDRezka Studio)", releases[0].Title);
    }

    [Fact]
    public async Task MergesQueriesWithoutDuplicates()
    {
        _prowlarr.Add("Дюна / Dune [2021, BDRip 1080p] Dub", seeders: 5);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.Equal(2, _prowlarr.Queries.Count);
        Assert.Single(releases);
    }

    [Fact]
    public async Task TolokaVoicesDefaultToUkrainian()
    {
        _prowlarr.Add("Дюна / Dune (2021) BDRip 1080p Dub", seeders: 5, indexer: "Toloka.to");

        var release = Assert.Single(await _search.SearchReleasesAsync(Dune, Token));

        Assert.Equal([new ReleaseAudio("ukr", VoiceType.Dub)], release.Audio);
    }

    [Fact]
    public async Task WarnsAboutTinyAndHugeReleases()
    {
        _prowlarr.Add("Dune.2021.1080p.WEB-DL.x264", seeders: 5, size: Gb);
        _prowlarr.Add("Dune.2021.1080p.BDRemux", seeders: 5, size: 30 * Gb);

        var releases = await _search.SearchReleasesAsync(Dune, Token);

        Assert.All(releases, release => Assert.Single(release.Warnings));
    }

    [Fact]
    public async Task AddRequestCarriesSeasonOfTheRelease()
    {
        _prowlarr.Add("Shogun.S02E03.1080p.WEB-DL", seeders: 5);
        var title = new TitleResult(MediaKind.Series, 126308, "Shogun", "Shōgun", 2024, null, "https://image/x.jpg", 8.6);

        var release = Assert.Single(await _search.SearchReleasesAsync(new ReleaseSearch(MediaKind.Series, "Shogun", "Shōgun"), Token));
        var request = release.ToAddRequest(title);

        Assert.Equal((MediaKind.Series, 126308, 2, 3), (request.Kind, request.TmdbId!.Value, request.Season!.Value, request.Episode!.Value));
        Assert.Equal("Shogun.S02E03.1080p.WEB-DL", request.ReleaseTitle);
    }

    private sealed class FakeProwlarr : IProwlarrClient
    {
        private readonly List<ProwlarrRelease> _releases = [];

        public List<string> Queries { get; } = [];

        public void Add(string title, int seeders, long size = 6 * Gb, string indexer = "RuTracker") =>
            _releases.Add(new ProwlarrRelease
            {
                Title = title,
                Seeders = seeders,
                Size = size,
                Indexer = indexer,
                Protocol = "torrent",
                MagnetUrl = $"magnet:?xt=urn:btih:{_releases.Count:x40}",
            });

        public Task<IReadOnlyList<ProwlarrRelease>> SearchAsync(string query, int categories, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<ProwlarrRelease>>(_releases);
        }
    }

    private sealed class FakeTmdb : ITmdbClient
    {
        public string? Language { get; private set; }

        public Task<IReadOnlyList<TitleResult>> SearchAsync(string query, string language, CancellationToken cancellationToken)
        {
            Language = language;
            return Task.FromResult<IReadOnlyList<TitleResult>>([]);
        }
    }
}
