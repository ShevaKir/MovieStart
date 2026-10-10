using System.Text.RegularExpressions;
using MovieStart.Agent.Downloads;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Library;

/// <summary>Which files of a torrent are worth downloading.</summary>
public static partial class FileSelection
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".ts",
    };

    public static bool IsVideo(string name) => VideoExtensions.Contains(Path.GetExtension(name));

    /// <summary>Samples, trailers and extras: in a folder named like that, or a file like "sample.mkv", "sample-x.mkv", "x-sample.mkv".</summary>
    public static bool IsJunk(string name)
    {
        var segments = name.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments[..^1].Any(folder => JunkFolder().IsMatch(folder.Trim()))
            || JunkFile().IsMatch(Path.GetFileNameWithoutExtension(name));
    }

    /// <summary>
    /// Files to skip as soon as the torrent's file list is known: junk everywhere, and for a movie every video but the main one.
    /// Subtitles and external audio tracks are kept.
    /// </summary>
    public static IReadOnlySet<int> AutoSkip(MediaKind kind, IReadOnlyList<QbitFile> files)
    {
        var skip = files.Where(file => IsJunk(file.Name)).Select(file => file.Index).ToHashSet();
        if (kind == MediaKind.Movie
            && files.Where(file => IsVideo(file.Name) && !skip.Contains(file.Index)).MaxBy(file => file.Size) is { } main)
        {
            skip.UnionWith(files.Where(file => IsVideo(file.Name) && file.Index != main.Index).Select(file => file.Index));
        }

        return skip;
    }

    /// <param name="downloadSeason">Season of the whole download; used when the file name has only the episode.</param>
    public static DownloadFile ToDownloadFile(QbitFile file, MediaKind kind, int? downloadSeason = null)
    {
        var isVideo = IsVideo(file.Name) && !IsJunk(file.Name);
        var (season, episode) = kind == MediaKind.Series && isVideo ? EpisodeParser.Parse(file.Name) : (null, null);
        if (episode is not null)
            season ??= downloadSeason;
        return new DownloadFile(file.Index, file.Name, file.Size, file.IsWanted, isVideo, season, episode);
    }

    [GeneratedRegex(@"^(?:samples?|extras?|featurettes?|bonus(?:es)?|trailers?|behind[ ._-]the[ ._-]scenes|deleted[ ._-]scenes)$", RegexOptions.IgnoreCase)]
    private static partial Regex JunkFolder();

    // Not "Sample.Show.S01E01": a word at the start counts only as the whole name or before a dash.
    [GeneratedRegex(@"^(?:sample|trailer)(?:-.*)?$|[.\-_ ](?:sample|trailer)$", RegexOptions.IgnoreCase)]
    private static partial Regex JunkFile();
}
