using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class DownloadViewModel(LibraryViewModel library, Guid itemId, LibraryDownload download)
    : ObservableObject, IKeyed<Guid>
{
    [ObservableProperty]
    private LibraryDownload _download = download;

    /// <summary>
    /// Only when the item has something left after it: a movie's single download goes with the whole item (the bin).
    /// </summary>
    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public Guid Key => Download.Id;

    public string Label => Download switch
    {
        { Season: { } s, Episode: { } e } => $"S{s:00}E{e:00}",
        { Season: { } s } => $"Season {s}",
        _ => "Release",
    };

    /// <summary>Full release name; long, so the view trims it.</summary>
    public string? ReleaseTitle => Download.ReleaseTitle;

    public string DeleteQuestion => Download.Season is null
        ? "Delete this release and its files from the Pi?"
        : $"Delete {Label} and its files from the Pi?";

    public bool IsActive => Download.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused;

    public bool IsPaused => Download.Status == DownloadStatus.Paused;

    public bool IsRunning => Download.Status is DownloadStatus.Queued or DownloadStatus.Downloading;

    public bool IsReady => Download.Status == DownloadStatus.Ready;

    public bool IsError => Download.Status == DownloadStatus.Error;

    public double Percent => Download.Progress * 100;

    public string StatusText => Download.Status switch
    {
        DownloadStatus.Queued => "Queued · waiting for metadata",
        DownloadStatus.Downloading => string.Join(" · ", new[]
        {
            $"{Percent:0}%",
            Format.Speed(Download.DownloadSpeed),
            Download.EtaSeconds is { } eta ? Format.Eta(eta) : null,
        }.Where(part => part is not null)),
        DownloadStatus.Paused => $"Paused · {Percent:0}%",
        DownloadStatus.Ready => Download.SizeBytes > 0 ? $"Downloaded · {Format.Size(Download.SizeBytes)}" : "Downloaded",
        _ => Download.Error ?? "Failed",
    };

    public void Update(LibraryDownload download)
    {
        Download = download;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task PauseAsync() => library.PauseDownloadAsync(itemId, Download.Id);

    [RelayCommand]
    private Task ResumeAsync() => library.ResumeDownloadAsync(itemId, Download.Id);

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        return library.DeleteDownloadAsync(itemId, Download.Id);
    }
}
