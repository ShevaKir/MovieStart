namespace MovieStart.Shared.Library;

/// <summary>
/// A movie or a series. A series collects every download of it (whole series, seasons, single episodes)
/// into one list of episodes.
/// </summary>
public sealed record LibraryItem
{
    public required Guid Id { get; init; }

    public MediaKind Kind { get; init; }

    public int? TmdbId { get; init; }

    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    public int? Year { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<LibraryDownload> Downloads { get; init; } = [];

    /// <summary>Playable files from finished downloads, in watch order (season, episode, name).</summary>
    public IReadOnlyList<MediaFile> Files { get; init; } = [];

    /// <summary>File played most recently; "continue watching" starts here.</summary>
    public int? LastPlayedFileId { get; init; }

    public DateTimeOffset? LastPlayedAt { get; init; }

    public DateTimeOffset AddedAt { get; init; }
}
