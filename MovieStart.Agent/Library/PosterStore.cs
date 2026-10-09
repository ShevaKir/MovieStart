using MovieStart.Shared.Library;

namespace MovieStart.Agent.Library;

/// <summary>
/// Keeps each item's TMDB poster in its directory, so it shows without TMDB and is deleted together with the item.
/// Fetched on first request, which also covers items added before posters were stored.
/// </summary>
public sealed class PosterStore(HttpClient http, ILibraryStore store, ILogger<PosterStore> logger)
{
    public const string FileName = "poster.jpg";

    /// <summary>Path of the stored poster, downloading it first if needed; null when there is none.</summary>
    public async Task<string?> GetAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        var path = Path.Combine(store.GetItemDirectory(item.Id), FileName);
        if (File.Exists(path))
            return path;
        if (item.PosterUrl is not { } url)
            return null;

        try
        {
            var bytes = await http.GetByteArrayAsync(url, cancellationToken);

            // Written aside and moved, so a parallel request never serves half a file.
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Poster for '{Title}' is not available: {Reason}", item.Title, ex.Message);
            return null;
        }
    }
}
