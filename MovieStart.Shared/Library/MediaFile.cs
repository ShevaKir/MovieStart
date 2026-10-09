namespace MovieStart.Shared.Library;

/// <summary>A playable video file of a library item: the movie itself or one episode.</summary>
/// <param name="Id">Stable within the item; never reused.</param>
/// <param name="DownloadId">Download the file came from.</param>
/// <param name="Name">Path relative to the item directory.</param>
/// <param name="Position">Last watched position in seconds.</param>
/// <param name="Duration">Duration in seconds; 0 until the file has been played.</param>
public sealed record MediaFile(
    int Id,
    Guid DownloadId,
    string Name,
    long SizeBytes,
    int? Season = null,
    int? Episode = null,
    double Position = 0,
    double Duration = 0,
    bool Watched = false)
{
    /// <summary>"S01E03" for episodes, otherwise null.</summary>
    public string? EpisodeCode => Season is { } s && Episode is { } e ? $"S{s:00}E{e:00}" : null;
}
