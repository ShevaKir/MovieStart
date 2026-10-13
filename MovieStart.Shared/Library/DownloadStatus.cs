using System.Text.Json.Serialization;

namespace MovieStart.Shared.Library;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    /// <summary>Added to the torrent client, waiting for metadata.</summary>
    Queued,

    /// <summary>
    /// A season or series release, stopped once its file list arrived (<see cref="LibraryDownload.Files"/> is null until
    /// then). Nothing is fetched until the episodes are chosen.
    /// </summary>
    ChoosingFiles,
    Downloading,
    Paused,

    /// <summary>Fully downloaded; can be played.</summary>
    Ready,
    Error,
}
