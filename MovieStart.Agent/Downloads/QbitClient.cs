using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace MovieStart.Agent.Downloads;

public interface IQbitClient
{
    /// <param name="source">Magnet link or .torrent URL.</param>
    /// <param name="tag">Tag used to find the torrent later; qBittorrent does not return the hash on add.</param>
    Task AddAsync(string source, string savePath, string tag, CancellationToken cancellationToken);

    Task<IReadOnlyList<QbitTorrent>> GetTorrentsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<QbitFile>> GetFilesAsync(string hash, CancellationToken cancellationToken);

    Task PauseAsync(string hash, CancellationToken cancellationToken);

    Task ResumeAsync(string hash, CancellationToken cancellationToken);

    Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken);
}

public sealed class QbitUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>qBittorrent WebUI API v2 client (https://github.com/qbittorrent/qBittorrent/wiki).</summary>
public sealed class QbitClient(HttpClient http, IOptions<QbitOptions> options) : IQbitClient
{
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    public Task AddAsync(string source, string savePath, string tag, CancellationToken cancellationToken) =>
        PostAsync("torrents/add", cancellationToken,
            ("urls", source), ("savepath", savePath), ("tags", tag));

    public async Task<IReadOnlyList<QbitTorrent>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("torrents/info")), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<QbitTorrent>>(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<QbitFile>> GetFilesAsync(string hash, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, Url($"torrents/files?hash={Uri.EscapeDataString(hash)}")), cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<QbitFile>>(cancellationToken) ?? [];
    }

    // qBittorrent 5 renamed pause/resume to stop/start; fall back for 4.x.
    public Task PauseAsync(string hash, CancellationToken cancellationToken) =>
        PostWithFallbackAsync("torrents/stop", "torrents/pause", cancellationToken, ("hashes", hash));

    public Task ResumeAsync(string hash, CancellationToken cancellationToken) =>
        PostWithFallbackAsync("torrents/start", "torrents/resume", cancellationToken, ("hashes", hash));

    public Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken) =>
        PostAsync("torrents/delete", cancellationToken, ("hashes", hash), ("deleteFiles", deleteFiles ? "true" : "false"));

    private async Task PostWithFallbackAsync(
        string method, string legacyMethod, CancellationToken cancellationToken, params (string Key, string Value)[] form)
    {
        using var response = await SendAsync(() => Post(method, form), cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
            await PostAsync(legacyMethod, cancellationToken, form);
    }

    private async Task PostAsync(string method, CancellationToken cancellationToken, params (string Key, string Value)[] form)
    {
        using var _ = await SendAsync(() => Post(method, form), cancellationToken);
    }

    /// <summary>Sends a request, logging in first or again when qBittorrent answers 403.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        try
        {
            var response = await http.SendAsync(createRequest(), cancellationToken);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();
                await LoginAsync(cancellationToken);
                response = await http.SendAsync(createRequest(), cancellationToken);
            }

            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
                return response;

            var status = (int)response.StatusCode;
            response.Dispose();
            throw new QbitUnavailableException($"qBittorrent answered {status}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new QbitUnavailableException($"qBittorrent is not reachable at {options.Value.BaseUrl}.", ex);
        }
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            using var response = await http.SendAsync(
                Post("auth/login", ("username", options.Value.Username), ("password", options.Value.Password)),
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode || !body.StartsWith("Ok", StringComparison.Ordinal))
                throw new QbitUnavailableException("qBittorrent login failed; check QBittorrent:Username and QBittorrent:Password.");
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private HttpRequestMessage Post(string method, params (string Key, string Value)[] form) =>
        new(HttpMethod.Post, Url(method))
        {
            Content = new FormUrlEncodedContent(form.Select(field => KeyValuePair.Create(field.Key, field.Value))),
        };

    private Uri Url(string method) => new(new Uri(options.Value.BaseUrl), $"/api/v2/{method}");
}
