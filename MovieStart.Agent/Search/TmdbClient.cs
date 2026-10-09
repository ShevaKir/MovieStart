using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MovieStart.Shared.Library;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Search;

public interface ITmdbClient
{
    /// <param name="language">TMDB language for titles and overviews, e.g. "uk-UA".</param>
    Task<IReadOnlyList<TitleResult>> SearchAsync(string query, string language, CancellationToken cancellationToken);
}

/// <summary>TMDB API v3 (https://developer.themoviedb.org/reference/search-multi).</summary>
public sealed class TmdbClient(HttpClient http, IOptions<TmdbOptions> options) : ITmdbClient
{
    public async Task<IReadOnlyList<TitleResult>> SearchAsync(string query, string language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ReadAccessToken))
            throw new SearchUnavailableException("TMDB is not configured: set Tmdb:ReadAccessToken.");

        var url = new Uri(new Uri(options.Value.BaseUrl),
            $"search/multi?include_adult=false&page=1&language={language}&query={Uri.EscapeDataString(query)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ReadAccessToken);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new SearchUnavailableException($"TMDB answered {(int)response.StatusCode}.");

            var page = await response.Content.ReadFromJsonAsync<SearchPage>(cancellationToken);
            return (page?.Results ?? []).Select(ToTitle).OfType<TitleResult>().ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SearchUnavailableException("TMDB is not reachable.", ex);
        }
    }

    private TitleResult? ToTitle(SearchItem item)
    {
        // "person" results are skipped.
        var kind = item.MediaType switch
        {
            "movie" => MediaKind.Movie,
            "tv" => MediaKind.Series,
            _ => (MediaKind?)null,
        };
        if (kind is null)
            return null;

        var title = item.Title ?? item.Name;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var date = item.ReleaseDate ?? item.FirstAirDate;
        return new TitleResult(
            kind.Value,
            item.Id,
            title,
            item.OriginalTitle ?? item.OriginalName,
            date is { Length: >= 4 } && int.TryParse(date[..4], out var year) ? year : null,
            string.IsNullOrWhiteSpace(item.Overview) ? null : item.Overview,
            item.PosterPath is null ? null : options.Value.ImageBaseUrl + item.PosterPath,
            item.VoteAverage is > 0 ? Math.Round(item.VoteAverage.Value, 1) : null);
    }

    private sealed record SearchPage([property: JsonPropertyName("results")] List<SearchItem>? Results);

    private sealed record SearchItem
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; init; }

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; init; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; init; }

        [JsonPropertyName("overview")]
        public string? Overview { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }

        [JsonPropertyName("vote_average")]
        public double? VoteAverage { get; init; }
    }
}
