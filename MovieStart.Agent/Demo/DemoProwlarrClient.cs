using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MovieStart.Agent.Search;

namespace MovieStart.Agent.Demo;

/// <summary>Makes up realistic releases (rutracker, Toloka, kinozal, scene) for whatever title is searched.</summary>
public sealed partial class DemoProwlarrClient : IProwlarrClient
{
    private const long Gb = 1024L * 1024 * 1024;
    private const int TvCategory = 5000;

    public Task<IReadOnlyList<ProwlarrRelease>> SearchAsync(string query, int categories, CancellationToken cancellationToken)
    {
        var match = TrailingYear().Match(query.Trim());
        var name = match.Groups["name"].Value.Trim();
        var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value) : 2024;

        // The agent searches the original and the localized title. Like the real trackers, Toloka answers to
        // the Ukrainian name and the others to the original one, so each query gets its own releases.
        var releases = categories == TvCategory ? Series(name, year) : Movies(name, year);
        var localized = Cyrillic().IsMatch(name);
        IReadOnlyList<ProwlarrRelease> answer = releases.Where(release => release.Indexer.StartsWith("Toloka") == localized).ToList();
        return Task.FromResult(answer);
    }

    private static List<ProwlarrRelease> Movies(string name, int year) =>
    [
        Release($"{name} / {name} [{year}, BDRip-HEVC 1080p, 10-bit] Dub + MVO (HDRezka Studio) + Ukr (Le Doyen) + Original Eng + Sub Rus, Ukr, Eng",
            "RuTracker", 7.4, 412),
        Release($"{name} ({year}) WEB-DL 1080p Ukr/Eng | Sub Ukr/Eng", "Toloka.to", 4.2, 138),
        Release($"{name} / {year} / BDRip-AVC (1080p) | D, P, A | Sub", "Kinozal", 11.6, 96),
        Release($"{name} ({year}) WEBRip 1080p MVO", "Rutor", 1.3, 540),
        Release($"{name} / {name} [{year}, BDRemux 1080p] Dub + Original Eng", "RuTracker", 29.4, 57),
        Release($"{name} [{year}, WEB-DL 2160p] Dub", "RuTracker", 19.8, 300),
        Release($"{name} ({year}) CAMRip", "Rutor", 1.4, 900),
    ];

    private static List<ProwlarrRelease> Series(string name, int year)
    {
        var releases = Enumerable.Range(1, 3)
            .Select(season => Release(
                $"{name} / {name} / Сезон: {season} / Серии: 1-8 из 8 [{year + season - 1}, WEB-DL 1080p] MVO (LostFilm) + Ukr (Dub) + Original + Sub (Rus, Eng)",
                "RuTracker", 8 * 1.6, 200 / season))
            .ToList();
        releases.Add(Release($"{name} (Сезон 1, серії 1-8) / {name} (Season 1) ({year}) WEB-DL 1080p Ukr/Eng", "Toloka.to", 8 * 1.2, 64));
        releases.Add(Release($"{name.Replace(' ', '.')}.S02E01.1080p.WEB-DL.H.265", "RuTracker", 1.4, 88));
        releases.Add(Release($"{name} / {name} / Сезоны: 1-3 / Серии: 1-24 из 24 [{year}-{year + 2}, WEB-DLRip 1080p] Dub", "Kinozal", 24 * 1.1, 45));
        return releases;
    }

    private static ProwlarrRelease Release(string title, string indexer, double sizeGb, int seeders)
    {
        var size = (long)(sizeGb * Gb);
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(title)));
        return new ProwlarrRelease
        {
            Title = title,
            Indexer = indexer,
            Size = size,
            Seeders = seeders,
            Protocol = "torrent",
            // dn and xl carry the name and size, so the simulated qBittorrent knows what it is "downloading".
            MagnetUrl = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(title)}&xl={size}",
        };
    }

    [GeneratedRegex(@"^(?<name>.*?)(?:\s+(?<year>(?:19|20)\d{2}))?$")]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex Cyrillic();
}
