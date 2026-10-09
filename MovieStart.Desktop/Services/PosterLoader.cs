using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace MovieStart.Desktop.Services;

public interface IPosterLoader
{
    /// <summary>Downloads a poster; returns null when it cannot be loaded.</summary>
    Task<Bitmap?> LoadAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class PosterLoader(HttpClient http) : IPosterLoader
{
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();

    public Task<Bitmap?> LoadAsync(string url, CancellationToken cancellationToken = default) =>
        _cache.GetOrAdd(url, _ => DownloadAsync(url));

    private async Task<Bitmap?> DownloadAsync(string url)
    {
        try
        {
            await using var stream = new MemoryStream(await http.GetByteArrayAsync(url));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            // A missing poster is not worth an error; the placeholder stays.
            return null;
        }
    }
}
