using System.Net;

namespace MovieStart.Agent.Tests;

public sealed class WebAppTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/items/7c9e6679-7425-40de-944b-e07fc1f90ae7")]
    public async Task ServesTheWebApp(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("_framework/blazor.webassembly", await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task UnknownApiRouteIsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/nothing-here", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
