using MovieStart.Shared.Library;

namespace MovieStart.Shared.Search;

/// <summary>A movie or series found in TMDB.</summary>
/// <param name="Title">Title in the language of the query.</param>
public sealed record TitleResult(
    MediaKind Kind,
    int TmdbId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    double? Rating);
