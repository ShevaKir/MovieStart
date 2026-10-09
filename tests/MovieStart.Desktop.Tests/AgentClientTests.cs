using System.Net;
using System.Net.Http.Json;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Health;
using MovieStart.Shared.Player;

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

    [Fact]
    public async Task ReturnsPlayerState()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PlayerState { IsConnected = true, Title = "Dune" }),
        });

        var state = await client.GetPlayerStateAsync("http://pi.local:5080", TestContext.Current.CancellationToken);

        Assert.Equal("Dune", state?.Title);
    }

    [Fact]
    public async Task SendsCommandWithTypeAsString()
    {
        string? body = null;
        var client = CreateClient(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await client.SendPlayerCommandAsync(
            "http://pi.local:5080", PlayerCommand.SeekRelative(-30), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("""{"type":"SeekRelative","value":-30}""", body);
    }

    [Fact]
    public async Task ReturnsProblemDetailOnFailure()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"status":503,"detail":"The player is not running."}"""),
        });

        var result = await client.PlayAsync("http://pi.local:5080", "/mnt/movies/Dune.mkv", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("The player is not running.", result.Error);
    }

    [Fact]
    public async Task CommandFailsWhenAgentIsUnreachable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("Connection refused"));

        var result = await client.SendPlayerCommandAsync("http://pi.local:5080", PlayerCommand.Stop(), TestContext.Current.CancellationToken);

        Assert.Equal("The agent is not reachable.", result.Error);
    }

    private static AgentClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
