using System.Text.Json.Serialization;

namespace MovieStart.Agent.Downloads;

/// <summary>Subset of <c>/api/v2/torrents/info</c>.</summary>
public sealed record QbitTorrent
{
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>0–1.</summary>
    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    /// <summary>Bytes per second.</summary>
    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; init; }

    /// <summary>Seconds; qBittorrent reports 8640000 when unknown.</summary>
    [JsonPropertyName("eta")]
    public long Eta { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    /// <summary>Size of the selected files in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("save_path")]
    public string SavePath { get; init; } = string.Empty;

    /// <summary>Comma-separated, e.g. "ms-1f2e, other".</summary>
    [JsonPropertyName("tags")]
    public string Tags { get; init; } = string.Empty;

    public bool HasTag(string tag) =>
        Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(tag);
}

/// <summary>Subset of <c>/api/v2/torrents/files</c>.</summary>
public sealed record QbitFile
{
    /// <summary>Position in the torrent; what <c>torrents/filePrio</c> takes as id.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>Path relative to the torrent save path.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    /// <summary>0–1.</summary>
    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    /// <summary>0 means the file is skipped.</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = QbitClient.NormalPriority;

    public bool IsWanted => Priority != QbitClient.SkipPriority;
}
