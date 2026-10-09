using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class DownloadViewModel(LibraryViewModel library, Guid itemId, LibraryDownload download)
    : ObservableObject, IKeyed<Guid>
{
    [ObservableProperty]
    private LibraryDownload _download = download;

    public Guid Key => Download.Id;

    public string Label => Download switch
    {
        { Season: { } s, Episode: { } e } => $"S{s:00}E{e:00}",
        { Season: { } s } => $"Season {s}",
        _ => Download.ReleaseTitle ?? "Download",
    };

    public string? ReleaseTitle => Download.Season is null ? null : Download.ReleaseTitle;

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
    private Task DeleteAsync() => library.DeleteDownloadAsync(itemId, Download.Id);
}
