using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Agent.Profile;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Library;

public sealed class LibraryService(
    ILibraryStore store,
    IQbitClient qbit,
    IPlayer player,
    IStorageProbe storage,
    IMediaProbe probe,
    IVoiceProfileStore profiles,
    IOptions<MediaOptions> media,
    TimeProvider time,
    ILogger<LibraryService> logger)
{
    /// <summary>Space kept free on top of the release size.</summary>
    public const long ReserveBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Resume a little before the saved position so the viewer can pick up the scene.</summary>
    private const double ResumeRewindSeconds = 5;

    /// <summary>Saved positions below this start from the beginning.</summary>
    private const double MinResumeSeconds = 30;

    /// <summary>Playback position is written to disk when it moves at least this far.</summary>
    private const double PositionSaveStepSeconds = 5;

    internal const long QbitUnknownEta = 8_640_000;

    /// <summary>qBittorrent adds torrents asynchronously; give it this long before calling a torrent missing.</summary>
    private static readonly TimeSpan AddGracePeriod = TimeSpan.FromMinutes(1);

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".ts",
    };

    private static readonly JsonSerializerOptions CompareOptions = new(JsonSerializerDefaults.Web);

    // Download progress and speed change every second; they live here and are written to disk only with other changes.
    private readonly ConcurrentDictionary<Guid, LibraryItem> _items = new();

    // Serializes read-modify-write of items between requests, the download sync and the watch tracker.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(media.Value.Root);
        foreach (var item in await store.LoadAllAsync(cancellationToken))
            _items[item.Id] = item;
        logger.LogInformation("Loaded {Count} library items from {Root}", _items.Count, media.Value.Root);
    }

    public IReadOnlyList<LibraryItem> List() =>
        _items.Values.OrderByDescending(item => item.LastPlayedAt ?? item.AddedAt).ToList();

    public StorageInfo GetStorage() => storage.Measure(media.Value.Root);

    public async Task<LibraryItem> AddAsync(AddToLibraryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.");
        if (!IsSupportedSource(request.Source))
            throw new ArgumentException("Source must be a magnet link or an http(s) .torrent URL.");
        if (request.Kind == MediaKind.Movie && (request.Season is not null || request.Episode is not null))
            throw new ArgumentException("A movie has no season or episode.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (request.SizeBytes is > 0 and var size)
                EnsureSpaceFor(size);

            var now = time.GetUtcNow();
            var item = FindExisting(request) ?? new LibraryItem
            {
                Id = Guid.NewGuid(),
                Kind = request.Kind,
                TmdbId = request.TmdbId,
                Title = request.Title.Trim(),
                OriginalTitle = request.OriginalTitle,
                Year = request.Year,
                PosterUrl = request.PosterUrl,
                AddedAt = now,
            };

            var download = new LibraryDownload
            {
                Id = Guid.NewGuid(),
                ReleaseTitle = request.ReleaseTitle,
                Season = request.Season,
                Episode = request.Episode,
                Status = DownloadStatus.Queued,
                SizeBytes = request.SizeBytes ?? 0,
                AddedAt = now,
            };

            var isNewItem = !_items.ContainsKey(item.Id);
            item = item with { Downloads = [.. item.Downloads, download] };
            await store.SaveAsync(item, cancellationToken);
            try
            {
                await qbit.AddAsync(request.Source, store.GetDownloadDirectory(item.Id, download.Id), TagFor(download.Id), cancellationToken);
            }
            catch
            {
                await RollBackAddAsync(item, download, isNewItem);
                throw;
            }

            _items[item.Id] = item;
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await DeleteItemAsync(Get(itemId), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteDownloadAsync(Guid itemId, Guid downloadId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var item = Get(itemId);
            var download = GetDownload(item, downloadId);
            if (item.Downloads.Count == 1)
            {
                // The last download takes the whole item with it.
                await DeleteItemAsync(item, cancellationToken);
                return;
            }

            var removedFiles = item.Files.Where(file => file.DownloadId == downloadId).ToList();
            if (removedFiles.Any(file => IsPlaying(item, file)))
                await StopPlayerAsync(cancellationToken);

            await DeleteTorrentAsync(download, await qbit.GetTorrentsAsync(cancellationToken), cancellationToken);
            store.DeleteDownload(itemId, downloadId);

            item = item with
            {
                Downloads = item.Downloads.Where(d => d.Id != downloadId).ToList(),
                Files = item.Files.Where(file => file.DownloadId != downloadId).ToList(),
                LastPlayedFileId = removedFiles.Any(file => file.Id == item.LastPlayedFileId) ? null : item.LastPlayedFileId,
            };
            await store.SaveAsync(item, cancellationToken);
            _items[itemId] = item;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task PauseDownloadAsync(Guid itemId, Guid downloadId, CancellationToken cancellationToken) =>
        qbit.PauseAsync(RequireActiveHash(itemId, downloadId), cancellationToken);

    public Task ResumeDownloadAsync(Guid itemId, Guid downloadId, CancellationToken cancellationToken) =>
        qbit.ResumeAsync(RequireActiveHash(itemId, downloadId), cancellationToken);

    /// <summary>Plays the requested file, or continues where the viewer stopped.</summary>
    /// <returns>The file that was started.</returns>
    public async Task<MediaFile> PlayAsync(Guid itemId, PlayItemRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var item = Get(itemId);
            var file = request.FileId is { } fileId
                ? item.Files.FirstOrDefault(f => f.Id == fileId) ?? throw new LibraryNotFoundException($"File {fileId} is not in '{item.Title}'.")
                : item.ChooseFileToContinue() ?? throw new LibraryStateException($"Nothing of '{item.Title}' is downloaded yet.");

            var start = request.FromStart || file.Watched || file.Position < MinResumeSeconds
                ? 0
                : file.Position - ResumeRewindSeconds;

            // Tracks are known once ffprobe has read the file; until then the player picks.
            var tracks = file.AudioTracks is null
                ? null
                : VoiceMatcher.Choose(profiles.Get(), file.AudioTracks, file.SubtitleTracks ?? []);
            var options = new PlaybackOptions(start, tracks?.AudioId, tracks is null ? null : new SubtitleChoice(tracks.SubtitleId));
            await player.PlayAsync(GetFilePath(item, file), options, cancellationToken);

            item = item with { LastPlayedFileId = file.Id, LastPlayedAt = time.GetUtcNow() };
            await store.SaveAsync(item, cancellationToken);
            _items[itemId] = item;
            return file;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Remembers how far the file at <paramref name="path"/> has been watched.</summary>
    public async Task RecordProgressAsync(string path, double position, double duration, CancellationToken cancellationToken)
    {
        if (TryFindFile(path) is not var (itemId, fileId))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_items.TryGetValue(itemId, out var item) || item.Files.FirstOrDefault(f => f.Id == fileId) is not { } file)
                return;

            var watched = file.Watched || (duration > 0 && position / duration >= WatchOrder.WatchedThreshold);
            if (Math.Abs(file.Position - position) < PositionSaveStepSeconds && watched == file.Watched
                && item.LastPlayedFileId == fileId && file.Duration == duration)
                return;

            var updated = file with { Position = position, Duration = duration, Watched = watched };
            item = item with
            {
                Files = item.Files.Select(f => f.Id == fileId ? updated : f).ToList(),
                LastPlayedFileId = fileId,
                LastPlayedAt = time.GetUtcNow(),
            };
            await store.SaveAsync(item, cancellationToken);
            _items[itemId] = item;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pulls download progress from qBittorrent and finalizes completed downloads.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var active = _items.Values
            .SelectMany(item => item.Downloads.Where(IsActive).Select(download => (item.Id, item.Kind, Download: download)))
            .ToList();
        if (active.Count == 0)
            return;

        // Talk to qBittorrent outside the lock; apply the results under it.
        var torrents = await qbit.GetTorrentsAsync(cancellationToken);
        var updates = new List<(Guid ItemId, LibraryDownload Download, IReadOnlyList<QbitFile>? CompletedFiles)>();
        foreach (var (itemId, kind, download) in active)
        {
            var torrent = FindTorrent(torrents, download.Id);
            var (updated, completed) = Track(download, torrent);
            IReadOnlyList<QbitFile>? files = null;
            if (completed)
            {
                files = await qbit.GetFilesAsync(torrent!.Hash, cancellationToken);
                // Downloads are not seeded from the Pi.
                await qbit.PauseAsync(torrent.Hash, cancellationToken);
            }

            updates.Add((itemId, updated, files));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var group in updates.GroupBy(update => update.ItemId))
            {
                // The item or download may have been deleted while we were talking to qBittorrent.
                if (!_items.TryGetValue(group.Key, out var item))
                    continue;

                var updated = item;
                foreach (var (_, download, files) in group)
                {
                    if (updated.Downloads.All(d => d.Id != download.Id))
                        continue;
                    updated = files is null ? ReplaceDownload(updated, download) : Complete(updated, download, files);
                }

                if (PersistentStateChanged(item, updated))
                    await store.SaveAsync(updated, cancellationToken);
                _items[item.Id] = updated;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads audio and subtitle tracks of downloaded files that have not been probed yet.</summary>
    /// <returns>Number of files probed.</returns>
    public async Task<int> ProbeNewFilesAsync(CancellationToken cancellationToken)
    {
        var pending = _items.Values
            .SelectMany(item => item.Files.Where(file => file.AudioTracks is null).Select(file => (item, file)))
            .ToList();

        foreach (var (item, file) in pending)
        {
            // ffprobe can take a moment; run it outside the lock.
            var result = await probe.ProbeAsync(GetFilePath(item, file), cancellationToken);

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_items.TryGetValue(item.Id, out var current) || current.Files.FirstOrDefault(f => f.Id == file.Id) is not { } latest)
                    continue;

                // An unreadable file gets empty lists so it is not probed again on every pass.
                var probed = latest with
                {
                    AudioTracks = result?.Audio ?? [],
                    SubtitleTracks = result?.Subtitles ?? [],
                    Duration = latest.Duration > 0 ? latest.Duration : result?.Duration ?? 0,
                };
                current = current with { Files = current.Files.Select(f => f.Id == file.Id ? probed : f).ToList() };
                await store.SaveAsync(current, cancellationToken);
                _items[current.Id] = current;
            }
            finally
            {
                _gate.Release();
            }
        }

        return pending.Count;
    }

    internal static DownloadStatus MapStatus(QbitTorrent torrent) => torrent.State switch
    {
        "error" or "missingFiles" => DownloadStatus.Error,
        "metaDL" or "forcedMetaDL" => DownloadStatus.Queued,
        "pausedDL" or "stoppedDL" => DownloadStatus.Paused,
        _ => DownloadStatus.Downloading,
    };

    internal static string TagFor(Guid downloadId) => $"ms-{downloadId:N}";

    private (LibraryDownload Download, bool Completed) Track(LibraryDownload download, QbitTorrent? torrent)
    {
        if (torrent is null)
        {
            if (time.GetUtcNow() - download.AddedAt < AddGracePeriod)
                return (download, false);
            return (download with { Status = DownloadStatus.Error, Error = "The torrent was removed from qBittorrent." }, false);
        }

        var updated = download with
        {
            TorrentHash = torrent.Hash,
            Status = MapStatus(torrent),
            Progress = torrent.Progress,
            SizeBytes = torrent.Size > 0 ? torrent.Size : download.SizeBytes,
            DownloadSpeed = torrent.DownloadSpeed,
            EtaSeconds = torrent.Eta is > 0 and < QbitUnknownEta ? torrent.Eta : null,
            Error = torrent.State is "error" or "missingFiles" ? $"qBittorrent reports '{torrent.State}'." : null,
        };
        return (updated, torrent.Progress >= 1);
    }

    private LibraryItem Complete(LibraryItem item, LibraryDownload download, IReadOnlyList<QbitFile> torrentFiles)
    {
        var videos = torrentFiles
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.Name)))
            .Where(file => !Path.GetFileName(file.Name).Contains("sample", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (videos.Count == 0)
            return ReplaceDownload(item, download with { Status = DownloadStatus.Error, Error = "The torrent has no video file." });

        // A movie torrent may carry extras; only the main file is kept.
        if (item.Kind == MediaKind.Movie)
            videos = [videos.MaxBy(file => file.Size)!];

        var nextId = item.Files.Count == 0 ? 1 : item.Files.Max(file => file.Id) + 1;
        var downloadFolder = download.Id.ToString("N");
        var added = videos.Select((file, index) =>
        {
            var (season, episode) = item.Kind == MediaKind.Series ? EpisodeParser.Parse(file.Name) : (null, null);
            return new MediaFile(
                nextId + index,
                download.Id,
                Path.Combine(downloadFolder, file.Name),
                file.Size,
                season ?? download.Season,
                episode ?? (videos.Count == 1 ? download.Episode : null));
        });

        logger.LogInformation("{Title}: {Release} is ready", item.Title, download.ReleaseTitle ?? download.Id.ToString());
        return ReplaceDownload(item, download with
        {
            Status = DownloadStatus.Ready,
            Progress = 1,
            DownloadSpeed = 0,
            EtaSeconds = null,
            Error = null,
        }) with
        {
            Files = WatchOrder.Sort([.. item.Files, .. added]),
        };
    }

    private static LibraryItem ReplaceDownload(LibraryItem item, LibraryDownload download) =>
        item with { Downloads = item.Downloads.Select(d => d.Id == download.Id ? download : d).ToList() };

    private LibraryItem? FindExisting(AddToLibraryRequest request) =>
        _items.Values.FirstOrDefault(item =>
            item.Kind == request.Kind
            && (request.TmdbId is not null
                ? item.TmdbId == request.TmdbId
                : string.Equals(item.Title, request.Title.Trim(), StringComparison.OrdinalIgnoreCase)));

    private async Task RollBackAddAsync(LibraryItem item, LibraryDownload download, bool isNewItem)
    {
        if (isNewItem)
        {
            store.Delete(item.Id);
            return;
        }

        store.DeleteDownload(item.Id, download.Id);
        await store.SaveAsync(_items[item.Id], CancellationToken.None);
    }

    private async Task DeleteItemAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        await StopPlayerIfPlayingAsync(item, cancellationToken);

        var torrents = await qbit.GetTorrentsAsync(cancellationToken);
        foreach (var download in item.Downloads)
            await DeleteTorrentAsync(download, torrents, cancellationToken);

        store.Delete(item.Id);
        _items.TryRemove(item.Id, out _);
    }

    private async Task DeleteTorrentAsync(LibraryDownload download, IReadOnlyList<QbitTorrent> torrents, CancellationToken cancellationToken)
    {
        var hash = download.TorrentHash ?? FindTorrent(torrents, download.Id)?.Hash;
        if (hash is not null)
            await qbit.DeleteAsync(hash, deleteFiles: true, cancellationToken);
    }

    private static QbitTorrent? FindTorrent(IReadOnlyList<QbitTorrent> torrents, Guid downloadId) =>
        torrents.FirstOrDefault(torrent => torrent.HasTag(TagFor(downloadId)));

    private static bool IsActive(LibraryDownload download) =>
        download.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused;

    // Download progress, speed and ETA are not worth a disk write on their own.
    private static bool PersistentStateChanged(LibraryItem before, LibraryItem after)
    {
        static LibraryItem WithoutLiveStats(LibraryItem item) => item with
        {
            Downloads = item.Downloads.Select(d => d with { Progress = 0, DownloadSpeed = 0, EtaSeconds = null }).ToList(),
        };

        return JsonSerializer.Serialize(WithoutLiveStats(before), CompareOptions)
            != JsonSerializer.Serialize(WithoutLiveStats(after), CompareOptions);
    }

    private static bool IsSupportedSource(string source) =>
        source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    private void EnsureSpaceFor(long sizeBytes)
    {
        // Downloads already running will still take the rest of their size.
        var pending = _items.Values
            .SelectMany(item => item.Downloads)
            .Where(IsActive)
            .Sum(download => (long)(download.SizeBytes * (1 - download.Progress)));
        var available = Math.Max(0, GetStorage().FreeBytes - pending - ReserveBytes);
        if (sizeBytes > available)
            throw new InsufficientStorageException(sizeBytes, available);
    }

    private string GetFilePath(LibraryItem item, MediaFile file) => Path.Combine(store.GetItemDirectory(item.Id), file.Name);

    /// <summary>Maps an absolute path under the media root back to its item and file.</summary>
    private (Guid ItemId, int FileId)? TryFindFile(string path)
    {
        foreach (var item in _items.Values)
        {
            var itemDirectory = store.GetItemDirectory(item.Id) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(itemDirectory, StringComparison.Ordinal))
                continue;

            var relative = path[itemDirectory.Length..];
            return item.Files.FirstOrDefault(file => file.Name == relative) is { } file ? (item.Id, file.Id) : null;
        }

        return null;
    }

    private LibraryItem Get(Guid itemId) =>
        _items.TryGetValue(itemId, out var item) ? item : throw new LibraryNotFoundException($"Item {itemId} is not in the library.");

    private static LibraryDownload GetDownload(LibraryItem item, Guid downloadId) =>
        item.Downloads.FirstOrDefault(d => d.Id == downloadId)
        ?? throw new LibraryNotFoundException($"Download {downloadId} is not part of '{item.Title}'.");

    private string RequireActiveHash(Guid itemId, Guid downloadId)
    {
        var download = GetDownload(Get(itemId), downloadId);
        if (!IsActive(download) || download.TorrentHash is null)
            throw new LibraryStateException("The download is not in progress.");
        return download.TorrentHash;
    }

    private bool IsPlaying(LibraryItem item, MediaFile file) => player.State.FilePath == GetFilePath(item, file);

    private async Task StopPlayerIfPlayingAsync(LibraryItem item, CancellationToken cancellationToken)
    {
        if (item.Files.Any(file => IsPlaying(item, file)))
            await StopPlayerAsync(cancellationToken);
    }

    private async Task StopPlayerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await player.SendAsync(PlayerCommand.Stop(), cancellationToken);
        }
        catch (PlayerUnavailableException)
        {
            // Nothing is playing if mpv is not running.
        }
    }
}
