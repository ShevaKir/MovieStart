using MovieStart.Agent.Downloads;

namespace MovieStart.Agent.Tests;

/// <summary>In-memory qBittorrent: torrents appear when added and are edited by the test.</summary>
public sealed class FakeQbitClient : IQbitClient
{
    public List<(string Source, string SavePath, string Tag)> Added { get; } = [];

    public List<QbitTorrent> Torrents { get; } = [];

    public Dictionary<string, List<QbitFile>> Files { get; } = [];

    public List<string> Paused { get; } = [];

    public List<string> Resumed { get; } = [];

    public List<(string Hash, bool DeleteFiles)> Deleted { get; } = [];

    public bool Unavailable { get; set; }

    public Task AddAsync(string source, string savePath, string tag, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        Added.Add((source, savePath, tag));
        return Task.CompletedTask;
    }

    /// <summary>Simulates qBittorrent picking up the last added torrent.</summary>
    public QbitTorrent Appear(string hash, string state = "downloading", double progress = 0, long size = 0)
    {
        var (_, savePath, tag) = Added[^1];
        var torrent = new QbitTorrent { Hash = hash, State = state, Progress = progress, Size = size, SavePath = savePath, Tags = tag };
        Torrents.Add(torrent);
        return torrent;
    }

    public void Update(string hash, Func<QbitTorrent, QbitTorrent> change)
    {
        var index = Torrents.FindIndex(t => t.Hash == hash);
        Torrents[index] = change(Torrents[index]);
    }

    public Task<IReadOnlyList<QbitTorrent>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult<IReadOnlyList<QbitTorrent>>(Torrents.ToList());
    }

    /// <summary>
    /// Files are numbered by position, like qBittorrent does, so tests can leave the index out.
    /// Wanted files without their own progress follow the torrent's.
    /// </summary>
    public Task<IReadOnlyList<QbitFile>> GetFilesAsync(string hash, CancellationToken cancellationToken)
    {
        var progress = Torrents.FirstOrDefault(t => t.Hash == hash)?.Progress ?? 0;
        return Task.FromResult<IReadOnlyList<QbitFile>>(
            Indexed(hash).Select(file => file.IsWanted && file.Progress == 0 ? file with { Progress = progress } : file).ToList());
    }

    public Task PauseAsync(string hash, CancellationToken cancellationToken)
    {
        Paused.Add(hash);
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string hash, CancellationToken cancellationToken)
    {
        Resumed.Add(hash);
        return Task.CompletedTask;
    }

    public List<string> Rechecked { get; } = [];

    public Task RecheckAsync(string hash, CancellationToken cancellationToken)
    {
        Rechecked.Add(hash);
        return Task.CompletedTask;
    }

    public Task SetFilePriorityAsync(string hash, IReadOnlyCollection<int> indexes, int priority, CancellationToken cancellationToken)
    {
        if (indexes.Count == 0)
            return Task.CompletedTask;
        if (Files.ContainsKey(hash))
            Files[hash] = Indexed(hash).Select(file => indexes.Contains(file.Index) ? file with { Priority = priority } : file).ToList();
        PriorityChanges.Add($"{hash}:{string.Join('|', indexes)}={priority}");
        return Task.CompletedTask;
    }

    /// <summary>"hash:1|2=0" per call, easy to compare.</summary>
    public List<string> PriorityChanges { get; } = [];

    private List<QbitFile> Indexed(string hash) =>
        (Files.GetValueOrDefault(hash) ?? []).Select((file, index) => file with { Index = index }).ToList();

    public Task DeleteAsync(string hash, bool deleteFiles, CancellationToken cancellationToken)
    {
        Deleted.Add((hash, deleteFiles));
        Torrents.RemoveAll(t => t.Hash == hash);
        return Task.CompletedTask;
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new QbitUnavailableException("qBittorrent is not reachable.");
    }
}
