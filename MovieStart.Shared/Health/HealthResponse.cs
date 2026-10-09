namespace MovieStart.Shared.Health;

/// <param name="IsDemo">The agent runs with simulated TMDB, Prowlarr, qBittorrent and mpv.</param>
public sealed record HealthResponse(string Version, DateTimeOffset ServerTime, bool IsDemo = false);
