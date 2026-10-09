using System.Net.Http.Json;
using System.Text.Json;
using MovieStart.Shared;
using MovieStart.Shared.Health;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.Services;

public sealed record AgentResult(bool IsSuccess, string? Error)
{
    public static AgentResult Success { get; } = new(true, null);

    public static AgentResult Failure(string error) => new(false, error);
}

public interface IAgentClient
{
    /// <summary>Returns the agent health, or <c>null</c> when the agent is unreachable.</summary>
    Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default);

    /// <summary>Returns the player state, or <c>null</c> when the agent is unreachable.</summary>
    Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default);

    Task<AgentResult> PlayAsync(string agentUrl, string path, CancellationToken cancellationToken = default);

    Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default);
}

public sealed class AgentClient(HttpClient http) : IAgentClient
{
    private const string Unreachable = "The agent is not reachable.";

    public Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetAsync<HealthResponse>(agentUrl, ApiRoutes.Health, cancellationToken);

    public Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetAsync<PlayerState>(agentUrl, ApiRoutes.PlayerState, cancellationToken);

    public Task<AgentResult> PlayAsync(string agentUrl, string path, CancellationToken cancellationToken = default) =>
        PostAsync(agentUrl, ApiRoutes.PlayerPlay, new PlayRequest(path), cancellationToken);

    public Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default) =>
        PostAsync(agentUrl, ApiRoutes.PlayerCommand, command, cancellationToken);

    private async Task<T?> GetAsync<T>(string agentUrl, string route, CancellationToken cancellationToken) where T : class
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return null;

        try
        {
            return await http.GetFromJsonAsync<T>(new Uri(baseUri, route), cancellationToken);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            return null;
        }
    }

    private async Task<AgentResult> PostAsync<T>(string agentUrl, string route, T body, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return AgentResult.Failure("The agent address is not a valid URL.");

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(baseUri, route), body, cancellationToken);
            if (response.IsSuccessStatusCode)
                return AgentResult.Success;

            return AgentResult.Failure(await ReadProblemDetailAsync(response, cancellationToken)
                ?? $"The agent answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            return AgentResult.Failure(Unreachable);
        }
    }

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return problem.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsConnectionFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;
}
