using System.Collections.Concurrent;
using System.Web;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Search;

namespace MovieStart.Agent.Demo;

/// <summary>
/// Pretends to be qBittorrent: progress grows with time, and when a download finishes its files
/// are written to disk as real demo clips.
/// </summary>
public sealed class DemoQbitClient(DemoClipFactory clips, IOptions<DemoOptions> options, TimeProvider time, ILogger<DemoQbitClient> logger)
    : IQbitClient
{
    private const int DefaultEpisodes = 8;
    private static readonly TimeSpan MetadataDelay = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, DemoTorrent> _torrents = new();

    public Task AddAsync(string source, string savePath, string tag, CancellationToken cancellationToken)
    {
        var (hash, title, size) = ReadSource(source);
        var parsed = ReleaseParser.Parse(title, title.Contains("Ukr/", StringComparison.OrdinalIgnoreCase) ? "ukr" : "rus");
        _torrents[hash] = new DemoTorrent(hash, title, savePath, tag, size, FilesFor(title, parsed, size), parsed, time.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QbitTorrent>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        IReadOnlyList<QbitTorrent> torrents = _torrents.Values.Select(torrent => Snapshot(torrent, now)).ToList();
        return Task.FromResult(torrents);
    }

    public Task<IReadOnlyList<QbitFile>> GetFilesAsync(string hash, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<QbitFile>>(_torrents.TryGetValue(hash, out var torrent) ? torrent.Files.ToList() : []);

    public Task RecheckAsync(string hash, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetFilePriorityAsync(string hash, IReadOnlyCollection<int> indexes, int priority, CancellationToken cancellationToken)
    {
        if (_torrents.TryGetValue(hash, out var torrent))
            torrent.SetPriority(indexes, priority);
        return Task.CompletedTask;
    }

    public Task PauseAsync(string hash, CancellationToken cancellationToken)
    {
        if (_torrents.TryGetValue(hash, out var torrent))
            torrent.Pause(time.GetUtcNow(), Duration);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string hash, CancellationToken cancellationToken)
    {
        if (_torrents.TryGetValue(hash, out var torrent))
            torrent.Resume(time.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
    {
        if (_torrents.TryRemove(hash, out var torrent) && deleteFiles && Directory.Exists(torrent.SavePath))
            Directory.Delete(torrent.SavePath, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>Finishes every running download right away (demo menu).</summary>
    public void CompleteAll()
    {
        foreach (var torrent in _torrents.Values)
            torrent.Complete();
    }

    private TimeSpan Duration => TimeSpan.FromSeconds(options.Value.DownloadSeconds);

    private QbitTorrent Snapshot(DemoTorrent torrent, DateTimeOffset now)
    {
        var progress = torrent.ProgressAt(now, Duration);
        string state;
        if (progress >= 1)
        {
            // Report completion only once the files really exist.
            if (!torrent.StartWriting(() => WriteFilesAsync(torrent)))
                progress = 0.999;
            state = torrent.IsPaused ? "pausedUP" : "stalledUP";
        }
        else if (now - torrent.AddedAt < MetadataDelay)
        {
            state = "metaDL";
        }
        else
        {
            state = torrent.IsPaused ? "pausedDL" : "downloading";
        }

        var running = state == "downloading";
        var speed = running ? (long)(torrent.Size / Duration.TotalSeconds) : 0;
        return new QbitTorrent
        {
            Hash = torrent.Hash,
            Name = torrent.Title,
            Progress = Math.Min(progress, 1),
            DownloadSpeed = speed,
            Eta = running ? (long)((1 - progress) * Duration.TotalSeconds) : Library.LibraryService.QbitUnknownEta,
            State = state,
            Size = torrent.Files.Where(file => file.IsWanted).Sum(file => file.Size),
            SavePath = torrent.SavePath,
            Tags = torrent.Tag,
        };
    }

    private async Task WriteFilesAsync(DemoTorrent torrent)
    {
        var (audio, subtitles) = DemoClipFactory.TracksFor(new ReleaseInfoLike(torrent.Parsed.Audio, torrent.Parsed.Subtitles));
        foreach (var file in torrent.Files.Where(file => file.IsWanted))
            await clips.CreateAsync(Path.Combine(torrent.SavePath, file.Name), audio, subtitles, CancellationToken.None);
        logger.LogInformation("Demo download finished: {Title}", torrent.Title);
    }

    private static (string Hash, string Title, long Size) ReadSource(string source)
    {
        if (!source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            return (Guid.NewGuid().ToString("N"), source, 2L * 1024 * 1024 * 1024);

        var query = HttpUtility.ParseQueryString(source["magnet:?".Length..]);
        var hash = query["xt"]?.Split(':').Last() ?? Guid.NewGuid().ToString("N");
        var size = long.TryParse(query["xl"], out var xl) ? xl : 2L * 1024 * 1024 * 1024;
        return (hash, query["dn"] ?? "Demo release", size);
    }

    /// <summary>Episode files for season releases, one main file plus a sample for movies.</summary>
    private static List<QbitFile> FilesFor(string title, ParsedRelease parsed, long size)
    {
        var name = string.Concat(title.Split(['/', '[', '(', '|'])[0].Trim().Split(Path.GetInvalidFileNameChars()));
        if (parsed.SeasonFrom is not { } firstSeason)
        {
            return
            [
                new QbitFile { Index = 0, Name = $"{name}/{name}.mkv", Size = size - 50_000_000 },
                new QbitFile { Index = 1, Name = $"{name}/Sample/sample.mkv", Size = 50_000_000 },
            ];
        }

        var lastSeason = parsed.SeasonTo ?? firstSeason;
        var single = firstSeason == lastSeason;
        var episodes = single && parsed.EpisodeFrom is { } from
            ? Enumerable.Range(from, (parsed.EpisodeTo ?? from) - from + 1)
            : Enumerable.Range(1, DefaultEpisodes);
        var files = (from season in Enumerable.Range(firstSeason, lastSeason - firstSeason + 1)
                     from episode in episodes
                     select $"{name}/Season {season}/{name}.S{season:00}E{episode:00}.mkv").ToList();
        return files.Select((file, index) => new QbitFile { Index = index, Name = file, Size = size / files.Count }).ToList();
    }

    private sealed class DemoTorrent(
        string hash, string title, string savePath, string tag, long size, List<QbitFile> files, ParsedRelease parsed, DateTimeOffset addedAt)
    {
        private readonly Lock _lock = new();
        private double _progressBeforePause;
        private DateTimeOffset _runningSince = addedAt;
        private bool _completed;
        private Task? _writing;

        public string Hash { get; } = hash;
        public string Title { get; } = title;
        public string SavePath { get; } = savePath;
        public string Tag { get; } = tag;
        public long Size { get; } = size;
        public IReadOnlyList<QbitFile> Files
        {
            get
            {
                lock (_lock)
                    return files.ToList();
            }
        }
        public ParsedRelease Parsed { get; } = parsed;
        public DateTimeOffset AddedAt { get; } = addedAt;
        public bool IsPaused { get; private set; }

        public double ProgressAt(DateTimeOffset now, TimeSpan duration)
        {
            lock (_lock)
            {
                if (_completed)
                    return 1;
                if (IsPaused)
                    return _progressBeforePause;
                var running = Math.Max(0, (now - _runningSince - MetadataDelay).TotalSeconds);
                return Math.Min(1, _progressBeforePause + running / duration.TotalSeconds);
            }
        }

        public void Pause(DateTimeOffset now, TimeSpan duration)
        {
            lock (_lock)
            {
                if (IsPaused)
                    return;
                _progressBeforePause = ProgressAt(now, duration);
                IsPaused = true;
            }
        }

        public void Resume(DateTimeOffset now)
        {
            lock (_lock)
            {
                IsPaused = false;
                _runningSince = now - MetadataDelay;
            }
        }

        public void SetPriority(IReadOnlyCollection<int> indexes, int priority)
        {
            lock (_lock)
            {
                for (var i = 0; i < files.Count; i++)
                {
                    if (indexes.Contains(files[i].Index))
                        files[i] = files[i] with { Priority = priority };
                }
            }
        }

        public void Complete()
        {
            lock (_lock)
                _completed = true;
        }

        /// <summary>Starts writing files on first call; true once they are written.</summary>
        public bool StartWriting(Func<Task> write)
        {
            lock (_lock)
                _writing ??= Task.Run(write);
            return _writing.IsCompletedSuccessfully;
        }
    }
}
