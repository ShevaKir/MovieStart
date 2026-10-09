using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MovieStart.Shared;
using MovieStart.Shared.Health;

namespace MovieStart.Agent.Tests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReturnsVersionAndServerTime()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ApiRoutes.Health, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(health);
        Assert.False(string.IsNullOrWhiteSpace(health.Version));
        Assert.DoesNotContain('+', health.Version);
        Assert.InRange(health.ServerTime, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
