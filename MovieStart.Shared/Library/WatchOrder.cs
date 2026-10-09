namespace MovieStart.Shared.Library;

public static class WatchOrder
{
    /// <summary>A file counts as watched once this share of it has been played.</summary>
    public const double WatchedThreshold = 0.92;

    /// <summary>Last played file, or the next one once it is finished; otherwise the first unwatched file.</summary>
    public static MediaFile? ChooseFileToContinue(this LibraryItem item)
    {
        var files = item.Files;
        if (files.Count == 0)
            return null;

        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].Id != item.LastPlayedFileId)
                continue;
            if (!files[i].Watched)
                return files[i];
            if (i + 1 < files.Count)
                return files[i + 1];
            break;
        }

        return files.FirstOrDefault(file => !file.Watched) ?? files[0];
    }

    /// <summary>Sorts files into watch order: season, episode, then name.</summary>
    public static IReadOnlyList<MediaFile> Sort(IEnumerable<MediaFile> files) =>
        files
            .OrderBy(file => file.Season ?? int.MaxValue)
            .ThenBy(file => file.Episode ?? int.MaxValue)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
