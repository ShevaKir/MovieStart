using System.Text.RegularExpressions;
using MovieStart.Shared.Library;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Search;

/// <param name="Title">Title as shown to the user (any language).</param>
/// <param name="Query">What the user typed; searched too when it differs from the titles.</param>
public sealed record ReleaseSearch(MediaKind Kind, string Title, string? OriginalTitle = null, int? Year = null, string? Query = null);

public sealed partial class SearchService(ITmdbClient tmdb, IProwlarrClient prowlarr)
{
    public const int TargetResolution = 1080;

    private const int MovieCategory = 2000;
    private const int TvCategory = 5000;
    private const long Gb = 1024L * 1024 * 1024;

    public Task<IReadOnlyList<TitleResult>> SearchTitlesAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required.");
        return tmdb.SearchAsync(query.Trim(), DetectLanguage(query), cancellationToken);
    }

    public async Task<IReadOnlyList<ReleaseInfo>> SearchReleasesAsync(ReleaseSearch search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search.Title))
            throw new ArgumentException("Title is required.");

        var category = search.Kind == MediaKind.Movie ? MovieCategory : TvCategory;
        var results = await Task.WhenAll(BuildQueries(search).Select(query => prowlarr.SearchAsync(query, category, cancellationToken)));

        return results
            .SelectMany(releases => releases)
            .Where(release => release.Protocol is null or "torrent")
            .DistinctBy(release => release.MagnetUrl ?? release.DownloadUrl ?? release.Title)
            .Select(ToReleaseInfo)
            .OfType<ReleaseInfo>()
            .Where(release => IsWanted(release, search))
            .OrderByDescending(QualityScore)
            .ThenByDescending(release => release.Seeders)
            .ToList();
    }

    /// <summary>TMDB language matching the alphabet of the query: Ukrainian, Russian or English.</summary>
    internal static string DetectLanguage(string query)
    {
        if (UkrainianLetters().IsMatch(query))
            return "uk-UA";
        return Cyrillic().IsMatch(query) ? "ru-RU" : "en-US";
    }

    internal static IReadOnlyList<string> BuildQueries(ReleaseSearch search)
    {
        // Series span several years, so the year would only narrow the results down to one season.
        var year = search.Kind == MediaKind.Movie && search.Year is { } y ? $" {y}" : string.Empty;
        return new[] { search.OriginalTitle, search.Title, search.Query }
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim() + year)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Higher is better: source first, then container, codec and a sensible size.</summary>
    internal static int QualityScore(ReleaseInfo release)
    {
        var score = release.Source switch
        {
            ReleaseSource.BdRip => 600,
            ReleaseSource.Remux or ReleaseSource.WebDl => 500,
            ReleaseSource.WebRip => 400,
            ReleaseSource.Unknown => 300,
            ReleaseSource.HdTv or ReleaseSource.HdRip => 200,
            _ => 100,
        };
        if (release.Container == "MKV")
            score += 20;
        if (release.Codec == "x265")
            score += 15;
        if (release.Is10Bit)
            score += 5;
        if (release.SeasonFrom is null && release.SizeBytes is >= 4 * Gb and <= 10 * Gb)
            score += 10;
        return score;
    }

    private static ReleaseInfo? ToReleaseInfo(ProwlarrRelease release)
    {
        var source = release.MagnetUrl ?? release.DownloadUrl;
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(release.Title))
            return null;

        // Toloka is Ukrainian: voice-overs without a language are Ukrainian there.
        var defaultLanguage = release.Indexer.Contains("toloka", StringComparison.OrdinalIgnoreCase) ? "ukr" : "rus";
        var parsed = ReleaseParser.Parse(release.Title, defaultLanguage);

        return new ReleaseInfo
        {
            Title = release.Title,
            Indexer = release.Indexer,
            DownloadSource = source,
            InfoUrl = release.InfoUrl,
            SizeBytes = release.Size,
            Seeders = release.Seeders ?? 0,
            PublishedAt = release.PublishDate,
            Resolution = parsed.Resolution,
            Source = parsed.Source,
            Codec = parsed.Codec,
            Is10Bit = parsed.Is10Bit,
            Container = parsed.Container,
            Audio = parsed.Audio,
            Subtitles = parsed.Subtitles,
            SeasonFrom = parsed.SeasonFrom,
            SeasonTo = parsed.SeasonTo,
            EpisodeFrom = parsed.EpisodeFrom,
            EpisodeTo = parsed.EpisodeTo,
            Warnings = Warnings(parsed, release.Size),
        };
    }

    private static IReadOnlyList<string> Warnings(ParsedRelease parsed, long size)
    {
        var warnings = new List<string>();
        if (parsed.Source == ReleaseSource.Remux)
            warnings.Add("Untouched Blu-ray: about 4× the size of a good rip.");
        if (parsed.SeasonFrom is null && size is > 0 and < 1536L * 1024 * 1024)
            warnings.Add("Small for 1080p: likely low quality.");
        return warnings;
    }

    private static bool IsWanted(ReleaseInfo release, ReleaseSearch search)
    {
        // The TV is 1080p; 4K only costs disk space and 720p looks worse.
        if (release.Resolution != TargetResolution || release.Source == ReleaseSource.Cam || release.Seeders <= 0)
            return false;

        // A season pack is not a movie.
        if (search.Kind == MediaKind.Movie && release.SeasonFrom is not null)
            return false;

        if (!MentionsTitle(release.Title, search))
            return false;

        // Remakes and sequels share titles; the year tells them apart.
        return search.Kind != MediaKind.Movie || search.Year is not { } year || !Years(release.Title).Any()
            || Years(release.Title).Any(y => Math.Abs(y - year) <= 1);
    }

    private static bool MentionsTitle(string releaseTitle, ReleaseSearch search)
    {
        var normalized = Normalize(releaseTitle);
        return new[] { search.OriginalTitle, search.Title, search.Query }
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Any(title => normalized.Contains(Normalize(title!), StringComparison.Ordinal));
    }

    // "Shōgun", "Shogun", "shogun." all compare equal.
    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var letters = decomposed.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return NonWord().Replace(new string(letters.ToArray()).ToLowerInvariant().Replace('ё', 'е'), " ").Trim();
    }

    private static IEnumerable<int> Years(string title) =>
        YearPattern().Matches(title).Select(match => int.Parse(match.Value));

    [GeneratedRegex("[іїєґІЇЄҐ]")]
    private static partial Regex UkrainianLetters();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex Cyrillic();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearPattern();
}
