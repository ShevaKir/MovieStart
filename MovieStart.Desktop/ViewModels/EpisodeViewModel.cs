using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

/// <summary>
/// One episode of a series: either a file on the Pi (<see cref="File"/>), or an episode of a season torrent
/// that is not on the Pi yet (<see cref="Download"/> and <see cref="Source"/>), which can be fetched.
/// </summary>
public sealed record EpisodeEntry(MediaFile? File, LibraryDownload? Download = null, DownloadFile? Source = null)
{
    public string Key => File is not null ? $"file:{File.Id}" : $"{Download!.Id:N}:{Source!.Index}";

    public int? Season => File is not null ? File.Season : Source!.Season;

    public int? Episode => File is not null ? File.Episode : Source!.Episode;
}

public partial class EpisodeViewModel(LibraryViewModel library, Guid itemId, EpisodeEntry entry)
    : ObservableObject, IKeyed<string>
{
    [ObservableProperty]
    private EpisodeEntry _entry = entry;

    [ObservableProperty]
    private bool _isLastPlayed;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public string Key => Entry.Key;

    /// <summary>Null while the episode is not on the Pi.</summary>
    public MediaFile? File => Entry.File;

    public bool IsOnPi => File is not null;

    public bool IsFetching => File is null && Entry.Source!.Wanted && Entry.Download!.Status != DownloadStatus.Ready;

    public bool CanFetch => File is null && !Entry.Source!.Wanted;

    public string Code => (File?.EpisodeCode ?? Entry.Source?.EpisodeCode) ?? "—";

    public string Name => Path.GetFileNameWithoutExtension(File?.Name ?? Entry.Source!.Name);

    public bool IsWatched => File?.Watched ?? false;

    /// <summary>What an episode that is not on the Pi is waiting for.</summary>
    public string? FetchStatus => File is not null
        ? null
        : IsFetching
            ? Entry.Download!.Status == DownloadStatus.Paused ? "Paused" : "Downloading…"
            : $"Not downloaded · {Format.Size(Entry.Source!.SizeBytes)}";

    public string DeleteQuestion => $"Delete {Code} from the Pi? You can download it again later.";

    /// <summary>0–100; how far the episode has been watched.</summary>
    public double WatchedPercent => File is null ? 0 : File.Watched ? 100 : File.Duration > 0 ? File.Position / File.Duration * 100 : 0;

    public string? Remaining => File is { Watched: false, Position: > 0, Duration: > 0 }
        ? $"{Format.Duration(File.Duration - File.Position)} left"
        : null;

    public void Update(EpisodeEntry entry, int? lastPlayedFileId)
    {
        Entry = entry;
        IsLastPlayed = entry.File is not null && entry.File.Id == lastPlayedFileId;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task PlayAsync() => File is null ? Task.CompletedTask : library.PlayAsync(itemId, new PlayItemRequest(File.Id));

    [RelayCommand]
    private Task PlayFromStartAsync() =>
        File is null ? Task.CompletedTask : library.PlayAsync(itemId, new PlayItemRequest(File.Id, FromStart: true));

    /// <summary>Adds the episode to what its torrent fetches; the episodes already chosen stay chosen.</summary>
    [RelayCommand]
    private Task FetchAsync()
    {
        if (Entry is not { File: null, Download: { Files: { } files } download, Source: { } source })
            return Task.CompletedTask;

        var wanted = files.Where(file => file.Wanted).Select(file => file.Index).Append(source.Index).ToList();
        return library.SelectDownloadFilesAsync(itemId, download.Id, wanted);
    }

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        return File is null ? Task.CompletedTask : library.DeleteFileAsync(itemId, File.Id);
    }
}
