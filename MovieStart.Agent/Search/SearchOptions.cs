namespace MovieStart.Agent.Search;

public sealed class ProwlarrOptions
{
    public const string SectionName = "Prowlarr";

    public string BaseUrl { get; set; } = "http://localhost:9696";

    /// <summary>Prowlarr → Settings → General → API Key.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";

    public string ImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p/w342";

    /// <summary>TMDB → Settings → API → "API Read Access Token".</summary>
    public string ReadAccessToken { get; set; } = string.Empty;
}

/// <summary>A search backend (TMDB, Prowlarr) is not configured or not reachable.</summary>
public sealed class SearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
