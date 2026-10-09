using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MovieStart.Agent.Search;

/// <summary>Subset of a Prowlarr <c>/api/v1/search</c> result.</summary>
public sealed record ProwlarrRelease
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("indexer")]
    public string Indexer { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("seeders")]
    public int? Seeders { get; init; }

    [JsonPropertyName("magnetUrl")]
    public string? MagnetUrl { get; init; }

    /// <summary>Prowlarr proxy link to the .torrent file.</summary>
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("infoUrl")]
    public string? InfoUrl { get; init; }

    [JsonPropertyName("publishDate")]
    public DateTimeOffset? PublishDate { get; init; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; init; }
}

public interface IProwlarrClient
{
    /// <param name="categories">Newznab categories: 2000 movies, 5000 TV.</param>
    Task<IReadOnlyList<ProwlarrRelease>> SearchAsync(string query, int categories, CancellationToken cancellationToken);
}

public sealed class ProwlarrClient(HttpClient http, IOptions<ProwlarrOptions> options) : IProwlarrClient
{
    public async Task<IReadOnlyList<ProwlarrRelease>> SearchAsync(string query, int categories, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
            throw new SearchUnavailableException("Prowlarr is not configured: set Prowlarr:ApiKey.");

        var url = new Uri(new Uri(options.Value.BaseUrl),
            $"/api/v1/search?type=search&limit=100&categories={categories}&query={Uri.EscapeDataString(query)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", options.Value.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new SearchUnavailableException($"Prowlarr answered {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<List<ProwlarrRelease>>(cancellationToken) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SearchUnavailableException($"Prowlarr is not reachable at {options.Value.BaseUrl}.", ex);
        }
    }
}
