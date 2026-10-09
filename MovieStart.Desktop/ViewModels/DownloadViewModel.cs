using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class DownloadViewModel : ObservableObject, IKeyed<Guid>
{
    private readonly LibraryViewModel _library;
    private readonly Guid _itemId;

    [ObservableProperty]
    private LibraryDownload _download;

    /// <summary>Set by the item: episodes can be picked only for series.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseEpisodes))]
    private bool _isSeries;

    [ObservableProperty]
    private bool _isChoosingEpisodes;

    public DownloadViewModel(LibraryViewModel library, Guid itemId, LibraryDownload download)
    {
        _library = library;
        _itemId = itemId;
        _download = download;
        SyncEpisodes();
    }

    /// <summary>
    /// Only when the item has something left after it: a movie's single download goes with the whole item (the bin).
    /// </summary>
    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public Guid Key => Download.Id;

    /// <summary>Episodes of the torrent while it downloads; untick one to skip it.</summary>
    public ObservableCollection<DownloadFileViewModel> Episodes { get; } = [];

    public bool CanChooseEpisodes => IsSeries && IsActive && Episodes.Count > 1;

    public string EpisodesChosen => $"Episodes: {Episodes.Count(episode => episode.IsWanted)} of {Episodes.Count}";

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
            Downloaded,
            Format.Speed(Download.DownloadSpeed),
            Download.EtaSeconds is { } eta ? Format.Eta(eta) : null,
        }.Where(part => part is not null)),
        DownloadStatus.Paused => string.Join(" · ", new[] { "Paused", $"{Percent:0}%", Downloaded }.Where(part => part is not null)),
        DownloadStatus.Ready => Download.SizeBytes > 0 ? $"Downloaded · {Format.Size(Download.SizeBytes)}" : "Downloaded",
        _ => Download.Error ?? "Failed",
    };

    /// <summary>"3.2 / 9.4 GB"; null until the torrent size is known.</summary>
    private string? Downloaded => Download.SizeBytes > 0
        ? Format.SizeProgress((long)(Download.SizeBytes * Download.Progress), Download.SizeBytes)
        : null;

    public void Update(LibraryDownload download)
    {
        Download = download;
        SyncEpisodes();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Sends the ticked episodes; files that are not episodes (subtitles, audio) keep their state.</summary>
    internal Task SendSelectionAsync()
    {
        OnPropertyChanged(nameof(EpisodesChosen));
        var ticked = Episodes.Where(episode => episode.IsWanted).Select(episode => episode.Key).ToHashSet();
        var wanted = (Download.Files ?? [])
            .Where(file => file.IsVideo ? ticked.Contains(file.Index) : file.Wanted)
            .Select(file => file.Index)
            .ToList();
        return _library.SelectDownloadFilesAsync(_itemId, Download.Id, wanted);
    }

    private void SyncEpisodes() =>
        CollectionSync.Sync(
            Episodes,
            Download.Files?.Where(file => file.IsVideo).ToList() ?? [],
            file => file.Index,
            file => new DownloadFileViewModel(this, file),
            (vm, file) => vm.Update(file));

    [RelayCommand]
    private Task PauseAsync() => _library.PauseDownloadAsync(_itemId, Download.Id);

    [RelayCommand]
    private Task ResumeAsync() => _library.ResumeDownloadAsync(_itemId, Download.Id);

    [RelayCommand]
    private void ToggleEpisodeChoice() => IsChoosingEpisodes = !IsChoosingEpisodes;

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        return _library.DeleteDownloadAsync(_itemId, Download.Id);
    }
}
