namespace MovieStart.Shared.Library;

/// <summary>One file inside a download's torrent, known once the torrent's metadata has arrived.</summary>
/// <param name="Index">Index of the file in the torrent; identifies it in selection requests.</param>
/// <param name="Name">Path inside the torrent.</param>
/// <param name="Wanted">False for files qBittorrent is told to skip.</param>
/// <param name="IsVideo">A video worth watching; false for samples, extras, subtitles and other files.</param>
public sealed record DownloadFile(
    int Index, string Name, long SizeBytes, bool Wanted, bool IsVideo = false, int? Season = null, int? Episode = null)
{
    /// <summary>"S01E03" for episodes, otherwise null.</summary>
    public string? EpisodeCode => Season is { } s && Episode is { } e ? $"S{s:00}E{e:00}" : null;
}
