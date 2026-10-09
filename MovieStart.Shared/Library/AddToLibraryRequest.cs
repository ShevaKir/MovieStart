namespace MovieStart.Shared.Library;

/// <summary>
/// Starts a download. A series download joins the existing series with the same TMDB id (or title),
/// so seasons and episodes can be added one by one.
/// </summary>
/// <param name="Source">Magnet link or .torrent URL.</param>
/// <param name="SizeBytes">Release size when known (from the search result); used for the free-space check.</param>
/// <param name="Season">Season the release covers; null for a movie or a complete series.</param>
/// <param name="Episode">Episode the release covers; null for a whole season.</param>
public sealed record AddToLibraryRequest(
    MediaKind Kind,
    string Title,
    string Source,
    long? SizeBytes = null,
    int? TmdbId = null,
    string? OriginalTitle = null,
    int? Year = null,
    string? PosterUrl = null,
    string? ReleaseTitle = null,
    int? Season = null,
    int? Episode = null);
