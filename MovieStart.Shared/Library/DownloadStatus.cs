using System.Text.Json.Serialization;

namespace MovieStart.Shared.Library;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadStatus>))]
public enum DownloadStatus
{
    /// <summary>Added to the torrent client, waiting for metadata.</summary>
    Queued,
    Downloading,
    Paused,

    /// <summary>Fully downloaded; can be played.</summary>
    Ready,
    Error,
}
