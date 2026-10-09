using System.Net;
using System.Net.Http.Json;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Health;

namespace MovieStart.Desktop.Tests;

public class AgentClientTests
{
    [Fact]
    public async Task ReturnsHealthWhenAgentResponds()
    {
        var expected = new HealthResponse("1.0.0", DateTimeOffset.UtcNow);
        Uri? requested = null;
        var client = CreateClient(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });

        var health = await client.GetHealthAsync("http://pi.local:5080", TestContext.Current.CancellationToken);

        Assert.Equal(expected, health);
        Assert.Equal("http://pi.local:5080/api/health", requested?.ToString());
    }

    [Fact]
    public async Task ReturnsNullWhenAgentIsUnreachable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("Connection refused"));

        Assert.Null(await client.GetHealthAsync("http://pi.local:5080", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReturnsNullOnServerError()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Null(await client.GetHealthAsync("http://pi.local:5080", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public async Task ReturnsNullForInvalidUrl(string agentUrl)
    {
        var client = CreateClient(_ => throw new InvalidOperationException("Must not send a request"));

        Assert.Null(await client.GetHealthAsync(agentUrl, TestContext.Current.CancellationToken));
    }

    private static AgentClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
