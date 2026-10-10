using System.Net.Http.Json;
using System.Text.Json;
using MovieStart.Shared;
using MovieStart.Shared.Health;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

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

    Task<AgentResult> SelectDownloadFilesAsync(
        string agentUrl, Guid itemId, Guid downloadId, SelectFilesRequest request, CancellationToken cancellationToken = default);

    Task<AgentResult> DeleteFileAsync(string agentUrl, Guid itemId, int fileId, CancellationToken cancellationToken = default);

    /// <summary>Starts playback on the TV; returns the file that was started.</summary>
    Task<AgentResult<MediaFile>> PlayItemAsync(string agentUrl, Guid itemId, PlayItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>Finds movies and series in TMDB; the query may be Russian, Ukrainian or English.</summary>
    Task<AgentResult<IReadOnlyList<TitleResult>>> SearchTitlesAsync(string agentUrl, string query, CancellationToken cancellationToken = default);

    /// <summary>Finds 1080p releases of a title, best first.</summary>
    Task<AgentResult<IReadOnlyList<ReleaseInfo>>> SearchReleasesAsync(
        string agentUrl, TitleResult title, string? query, CancellationToken cancellationToken = default);

    Task<AgentResult<VoiceProfile>> GetVoiceProfileAsync(string agentUrl, CancellationToken cancellationToken = default);

    Task<AgentResult> SaveVoiceProfileAsync(string agentUrl, VoiceProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Demo mode only: finish all simulated downloads.</summary>
    Task<AgentResult> CompleteDemoDownloadsAsync(string agentUrl, CancellationToken cancellationToken = default);

    /// <summary>Demo mode only: delete the demo library.</summary>
    Task<AgentResult> ResetDemoAsync(string agentUrl, CancellationToken cancellationToken = default);
}

/// <param name="http">Should have no timeout of its own; each call sets one.</param>
public sealed class AgentClient(HttpClient http) : IAgentClient
{
    private const string Unreachable = "The agent is not reachable.";

    // Polls and commands must fail fast so "offline" shows quickly; release search fans out to trackers.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(45);

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

    public Task<AgentResult> SelectDownloadFilesAsync(
        string agentUrl, Guid itemId, Guid downloadId, SelectFilesRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Put, ApiRoutes.LibraryDownloadFiles(itemId, downloadId), cancellationToken, JsonContent.Create(request));

    public Task<AgentResult> DeleteFileAsync(string agentUrl, Guid itemId, int fileId, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Delete, ApiRoutes.LibraryFile(itemId, fileId), cancellationToken);

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

    public Task<AgentResult<IReadOnlyList<TitleResult>>> SearchTitlesAsync(
        string agentUrl, string query, CancellationToken cancellationToken = default) =>
        GetResultAsync<IReadOnlyList<TitleResult>>(
            agentUrl, $"{ApiRoutes.SearchTitles}?query={Uri.EscapeDataString(query)}", cancellationToken, SearchTimeout);

    public Task<AgentResult<IReadOnlyList<ReleaseInfo>>> SearchReleasesAsync(
        string agentUrl, TitleResult title, string? query, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["kind"] = title.Kind.ToString(),
            ["title"] = title.Title,
            ["originalTitle"] = title.OriginalTitle,
            ["year"] = title.Year?.ToString(),
            ["query"] = query,
        };
        var queryString = string.Join("&", parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}"));
        return GetResultAsync<IReadOnlyList<ReleaseInfo>>(agentUrl, $"{ApiRoutes.SearchReleases}?{queryString}", cancellationToken, SearchTimeout);
    }

    public Task<AgentResult<VoiceProfile>> GetVoiceProfileAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        GetResultAsync<VoiceProfile>(agentUrl, ApiRoutes.VoiceProfile, cancellationToken, DefaultTimeout);

    public Task<AgentResult> SaveVoiceProfileAsync(string agentUrl, VoiceProfile profile, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Put, ApiRoutes.VoiceProfile, cancellationToken, JsonContent.Create(profile));

    public Task<AgentResult> CompleteDemoDownloadsAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Post, ApiRoutes.DemoCompleteDownloads, cancellationToken);

    public Task<AgentResult> ResetDemoAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        SendAsync(agentUrl, HttpMethod.Post, ApiRoutes.DemoReset, cancellationToken, timeout: SearchTimeout);

    /// <summary>GET that reports why it failed, unlike the polling reads that just return null.</summary>
    private async Task<AgentResult<T>> GetResultAsync<T>(string agentUrl, string route, CancellationToken cancellationToken, TimeSpan timeout)
    {
        T? value = default;
        var result = await SendAsync(
            agentUrl,
            HttpMethod.Get,
            route,
            cancellationToken,
            readSuccess: async response => value = await response.Content.ReadFromJsonAsync<T>(cancellationToken),
            timeout: timeout);
        return new AgentResult<T>(value, result.Error);
    }

    private async Task<T?> GetAsync<T>(string agentUrl, string route, CancellationToken cancellationToken) where T : class
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return null;

        using var timeout = Timeout(DefaultTimeout, cancellationToken);
        try
        {
            return await http.GetFromJsonAsync<T>(new Uri(baseUri, route), timeout.Token);
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
        Func<HttpResponseMessage, Task>? readSuccess = null,
        TimeSpan? timeout = null)
    {
        if (!Uri.TryCreate(agentUrl, UriKind.Absolute, out var baseUri))
            return AgentResult.Failure("The agent address is not a valid URL.");

        using var deadline = Timeout(timeout ?? DefaultTimeout, cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(baseUri, route)) { Content = content };
            using var response = await http.SendAsync(request, deadline.Token);
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

    private static CancellationTokenSource Timeout(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static bool IsConnectionFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;
}
