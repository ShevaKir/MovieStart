using System.Text.RegularExpressions;

namespace MovieStart.Agent.Library;

/// <summary>Reads season and episode numbers from release file paths.</summary>
public static partial class EpisodeParser
{
    public static (int? Season, int? Episode) Parse(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);

        if (SeasonEpisode().Match(fileName) is { Success: true } se)
            return (int.Parse(se.Groups["s"].Value), int.Parse(se.Groups["e"].Value));

        if (CrossFormat().Match(fileName) is { Success: true } x)
            return (int.Parse(x.Groups["s"].Value), int.Parse(x.Groups["e"].Value));

        // "Season 2/03. Title.mkv" or "S02/E03.mkv": season from a folder, episode from the file.
        var season = SeasonFolder().Match(Path.GetDirectoryName(path) ?? string.Empty) is { Success: true } folder
            ? int.Parse(folder.Groups["s"].Value)
            : (int?)null;
        var episode = EpisodeOnly().Match(fileName) is { Success: true } e ? int.Parse(e.Groups["e"].Value) : (int?)null;
        return (season, episode);
    }

    // S01E02, s1e2, S01.E02, S01 E02
    [GeneratedRegex(@"\bS(?<s>\d{1,2})[ ._-]?E(?<e>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisode();

    // 1x02
    [GeneratedRegex(@"\b(?<s>\d{1,2})x(?<e>\d{2,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CrossFormat();

    // "Season 2", "S02", "Сезон 2", "2 сезон"
    [GeneratedRegex(@"(?:\bSeason[ ._-]?(?<s>\d{1,2})\b|\bS(?<s>\d{1,2})\b|Сезон[ ._-]?(?<s>\d{1,2})|(?<s>\d{1,2})[ ._-]?сезон)", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolder();

    // "E03", "Episode 3", "03. Title", "Серия 3"
    [GeneratedRegex(@"(?:\bE(?:pisode)?[ ._-]?(?<e>\d{1,3})\b|^(?<e>\d{1,3})(?:[ ._-]|$)|Серия[ ._-]?(?<e>\d{1,3}))", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeOnly();
}
