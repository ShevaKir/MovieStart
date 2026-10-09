namespace MovieStart.Shared.Health;

public sealed record HealthResponse(string Version, DateTimeOffset ServerTime);
