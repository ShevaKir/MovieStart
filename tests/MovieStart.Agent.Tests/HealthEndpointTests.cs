using System.Net;
using System.Net.Http.Json;
using MovieStart.Shared;
using MovieStart.Shared.Health;

namespace MovieStart.Agent.Tests;

public sealed class HealthEndpointTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ReturnsVersionAndServerTime()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ApiRoutes.Health, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(health);
        Assert.False(string.IsNullOrWhiteSpace(health.Version));
        Assert.DoesNotContain('+', health.Version);
        Assert.InRange(health.ServerTime, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
