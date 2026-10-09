using System.Net.Http.Json;
using System.Text.Json;
using MovieStart.Shared;
using MovieStart.Shared.Health;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.Services;

public record AgentResult(bool IsSuccess, string? Error)
{
    public static AgentResult Success { get; } = new(true, null);

    public static AgentResult Failure(string error) => new(false, error);
}

public sealed record AgentResult<T>(T? Value, string? Error) : AgentResult(Error is null, Error);

public interface IAgentClient
{
    /// <summary>Returns the agent health, or <c>null</c> when the agent is unreachable.</summary>
    Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default);

    /// <summary>Returns the player state, or <c>null</c> when the agent is unreachable.</summary>
    Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default);

    Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default);

    /// <summary>Returns the library, or <c>null</c> when the agent is unreachable.</summary>
    Task<IReadOnlyList<LibraryItem>?> GetLibraryAsync(string agentUrl, CancellationToken cancellationToken = default);

    /// <summary>Returns disk usage, or <c>null</c> when the agent is unreachable.</summary>
    Task<StorageInfo?> GetStorageAsync(string agentUrl, CancellationToken cancellationToken = default);

    Task<AgentResult> AddToLibraryAsync(string agentUrl, AddToLibraryRequest request, CancellationToken cancellationToken = default);

    Task<AgentResult> DeleteItemAsync(string agentUrl, Guid itemId, CancellationToken cancellationToken = default);

    Task<AgentResult> DeleteDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default);

    Task<AgentResult> PauseDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default);

    Task<AgentResult> ResumeDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default);

    /// <summary>Starts playback on the TV; returns the file that was started.</summary>
    Task<AgentResult<MediaFile>> PlayItemAsync(string agentUrl, Guid itemId, PlayItemRequest request, CancellationToken cancellationToken = default);
}

public sealed class AgentClient(HttpClient http) : IAgentClient
{
    private const string Unreachable = "The agent is not reachable.";

    public Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetAsync<HealthResponse>(agentUrl, ApiRoutes.Health, cancellationToken);

    public Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetAsync<PlayerState>(agentUrl, ApiRoutes.PlayerState, cancellationToken);

    public Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default) =>
        PostAsync(agentUrl, ApiRoutes.PlayerCommand, command, cancellationToken);

    public async Task<IReadOnlyList<LibraryItem>?> GetLibraryAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        await GetAsync<List<LibraryItem>>(agentUrl, ApiRoutes.Library, cancellationToken);

    public Task<StorageInfo?> GetStorageAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetAsync<StorageInfo>(agentUrl, ApiRoutes.Storage, cancellationToken);

    public Task<AgentResult> AddToLibraryAsync(string agentUrl, AddToLibraryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(agentUrl, ApiRoutes.Library, request, cancellationToken);

    public Task<AgentResult> DeleteItemAsync(string agentUrl, Guid itemId, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Delete, ApiRoutes.LibraryItem(itemId), cancellationToken);

    public Task<AgentResult> DeleteDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Delete, ApiRoutes.LibraryDownload(itemId, downloadId), cancellationToken);

    public Task<AgentResult> PauseDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Post, ApiRoutes.LibraryDownloadPause(itemId, downloadId), cancellationToken);

    public Task<AgentResult> ResumeDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Post, ApiRoutes.LibraryDownloadResume(itemId, downloadId), cancellationToken);

    public async Task<AgentResult<MediaFile>> PlayItemAsync(
        string agentUrl, Guid itemId, PlayItemRequest request, CancellationToken cancellationToken = default)
    {
        MediaFile? started = null;
        var result = await SendAsync(
            agentUrl,
            HttpMethod.Post,
            ApiRoutes.LibraryPlay(itemId),
            cancellationToken,
            JsonContent.Create(request),
            async response => started = await response.Content.ReadFromJsonAsync<MediaFile>(cancellationToken));
        return new AgentResult<MediaFile>(started, result.Error);
    }

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

    private Task<AgentResult> PostAsync<T>(string agentUrl, string route, T body, CancellationToken cancellationToken) =>
        SendAsync(agentUrl, HttpMethod.Post, route, cancellationToken, JsonContent.Create(body));

    private async Task<AgentResult> SendAsync(
        string agentUrl,
        HttpMethod method,
        string route,
        CancellationToken cancellationToken,
        HttpContent? content = null,
        Func<HttpResponseMessage, Task>? readSuccess = null)
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return AgentResult.Failure("The agent address is not a valid URL.");

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(baseUri, route)) { Content = content };
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return AgentResult.Failure(await ReadProblemDetailAsync(response, cancellationToken)
                    ?? $"The agent answered {(int)response.StatusCode}.");
            }

            if (readSuccess is not null)
                await readSuccess(response);
            return AgentResult.Success;
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
