namespace MovieStart.Shared.Library;

/// <summary>One torrent of a library item: a movie, a whole series, a season or a single episode.</summary>
public sealed record LibraryDownload
{
    public required Guid Id { get; init; }

    /// <summary>Name of the torrent release, e.g. "Shogun.S01.1080p.WEB-DL.x265".</summary>
    public string? ReleaseTitle { get; init; }

    /// <summary>Season the release covers; null for a movie or a complete series.</summary>
    public int? Season { get; init; }

    /// <summary>Episode the release covers; null for a whole season, series or a movie.</summary>
    public int? Episode { get; init; }

    public DownloadStatus Status { get; init; }

    /// <summary>Download progress, 0–1.</summary>
    public double Progress { get; init; }

    /// <summary>Total size in bytes; 0 until known.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Current download speed in bytes per second.</summary>
    public long DownloadSpeed { get; init; }

    /// <summary>Estimated seconds left; null when unknown.</summary>
    public long? EtaSeconds { get; init; }

    /// <summary>Known once the torrent client has the torrent.</summary>
    public string? TorrentHash { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset AddedAt { get; init; }
}
