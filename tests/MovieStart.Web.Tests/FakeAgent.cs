using System.Net;
using System.Net.Http.Json;
using MovieStart.Client;
using MovieStart.Web.State;

namespace MovieStart.Web.Tests;

/// <summary>A real <see cref="AgentClient"/> talking to canned responses, keyed by method and path.</summary>
public sealed class FakeAgent : HttpMessageHandler
{
    public const string Url = "http://pi.local:5080/";

    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];

    public FakeAgent() => Client = new AgentClient(new HttpClient(this));

    public IAgentClient Client { get; }

    public AgentAddress Address { get; } = new(Url);

    public List<(string Request, string? Body)> Requests { get; } = [];

    public void Reply<T>(string method, string path, T body) =>
        _responses[$"{method} {path}"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    public void Fail(string method, string path, HttpStatusCode status, string detail) =>
        _responses[$"{method} {path}"] = () => new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(new { detail }),
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((key, body));
        return _responses.TryGetValue(key, out var response)
            ? response()
            : request.Method == HttpMethod.Get
                ? throw new HttpRequestException("Unreachable")
                : new HttpResponseMessage(HttpStatusCode.NoContent);
    }
}
