using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Media;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Library;

public interface ILibraryStore
{
    /// <summary>Directory that holds the item's <c>item.json</c> and one subdirectory per download.</summary>
    string GetItemDirectory(Guid itemId);

    string GetDownloadDirectory(Guid itemId, Guid downloadId);

    Task<IReadOnlyList<LibraryItem>> LoadAllAsync(CancellationToken cancellationToken);

    Task SaveAsync(LibraryItem item, CancellationToken cancellationToken);

    /// <summary>Deletes the item directory with everything in it.</summary>
    void Delete(Guid itemId);

    void DeleteDownload(Guid itemId, Guid downloadId);
}

/// <summary>
/// Keeps each item's metadata in <c>{Media:Root}/{itemId}/item.json</c> next to its files,
/// so the library lives on the movies disk and needs no database.
/// </summary>
public sealed class FileLibraryStore(IOptions<MediaOptions> media, ILogger<FileLibraryStore> logger) : ILibraryStore
{
    private const string MetadataFile = "item.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string GetItemDirectory(Guid itemId) => Path.Combine(media.Value.Root, itemId.ToString("N"));

    public string GetDownloadDirectory(Guid itemId, Guid downloadId) =>
        Path.Combine(GetItemDirectory(itemId), downloadId.ToString("N"));

    public async Task<IReadOnlyList<LibraryItem>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(media.Value.Root))
            return [];

        var items = new List<LibraryItem>();
        foreach (var directory in Directory.EnumerateDirectories(media.Value.Root))
        {
            var file = Path.Combine(directory, MetadataFile);
            if (!File.Exists(file))
                continue;

            try
            {
                await using var stream = File.OpenRead(file);
                if (await JsonSerializer.DeserializeAsync<LibraryItem>(stream, JsonOptions, cancellationToken) is { } item)
                    items.Add(item);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping unreadable {File}", file);
            }
        }

        return items;
    }

    public async Task SaveAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        var directory = GetItemDirectory(item.Id);
        Directory.CreateDirectory(directory);

        // Write to a temp file first so a crash never leaves a half-written item.json.
        var file = Path.Combine(directory, MetadataFile);
        var temp = file + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, item, JsonOptions, cancellationToken);
        File.Move(temp, file, overwrite: true);
    }

    public void Delete(Guid itemId) => DeleteDirectory(GetItemDirectory(itemId));

    public void DeleteDownload(Guid itemId, Guid downloadId) => DeleteDirectory(GetDownloadDirectory(itemId, downloadId));

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
