using System.Net.Http.Json;
using MovieStart.Shared;
using MovieStart.Shared.Health;

namespace MovieStart.Desktop.Services;

public interface IAgentClient
{
    /// <summary>Returns the agent health, or <c>null</c> when the agent is unreachable.</summary>
    Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default);
}

public sealed class AgentClient(HttpClient http) : IAgentClient
{
    public async Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return null;

        try
        {
            return await http.GetFromJsonAsync<HealthResponse>(new Uri(baseUri, ApiRoutes.Health), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
