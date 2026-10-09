using MovieStart.Shared.Library;

namespace MovieStart.Shared.Search;

/// <param name="Language">ISO 639-2 code: "ukr", "rus", "eng".</param>
/// <param name="Studio">Studio or translator, e.g. "HDRezka", "Гаврилов".</param>
public sealed record ReleaseAudio(string Language, VoiceType Type, string? Studio = null);

/// <summary>A torrent release found by the indexers, with what could be read from its title.</summary>
public sealed record ReleaseInfo
{
    public required string Title { get; init; }

    public required string Indexer { get; init; }

    /// <summary>Magnet link or .torrent URL to hand to the library.</summary>
    public required string DownloadSource { get; init; }

    public string? InfoUrl { get; init; }

    public long SizeBytes { get; init; }

    public int Seeders { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Vertical resolution, e.g. 1080; null when the title does not say.</summary>
    public int? Resolution { get; init; }

    public ReleaseSource Source { get; init; }

    /// <summary>"x265", "x264" or "AV1"; null when unknown.</summary>
    public string? Codec { get; init; }

    public bool Is10Bit { get; init; }

    /// <summary>"MKV", "MP4" or "AVI"; null when unknown.</summary>
    public string? Container { get; init; }

    public IReadOnlyList<ReleaseAudio> Audio { get; init; } = [];

    /// <summary>Subtitle languages, ISO 639-2.</summary>
    public IReadOnlyList<string> Subtitles { get; init; } = [];

    /// <summary>First season the release covers; null for movies and complete series without numbers.</summary>
    public int? SeasonFrom { get; init; }

    public int? SeasonTo { get; init; }

    public int? EpisodeFrom { get; init; }

    public int? EpisodeTo { get; init; }

    /// <summary>Single season covered, or null when several or unknown.</summary>
    public int? Season => SeasonFrom is { } from && (SeasonTo is null || SeasonTo == from) ? from : null;

    /// <summary>Single episode covered, or null when several or unknown.</summary>
    public int? Episode => Season is not null && EpisodeFrom is { } from && (EpisodeTo is null || EpisodeTo == from) ? from : null;

    /// <summary>Hints for the viewer, e.g. "Small for 1080p: likely low quality".</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Builds the request that downloads this release into the library.</summary>
    public AddToLibraryRequest ToAddRequest(TitleResult title) => new(
        title.Kind,
        title.Title,
        DownloadSource,
        SizeBytes > 0 ? SizeBytes : null,
        title.TmdbId,
        title.OriginalTitle,
        title.Year,
        title.PosterUrl,
        Title,
        title.Kind == MediaKind.Series ? Season : null,
        title.Kind == MediaKind.Series ? Episode : null);
}
