using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class EpisodeViewModel(LibraryViewModel library, Guid itemId, MediaFile file)
    : ObservableObject, IKeyed<int>
{
    [ObservableProperty]
    private MediaFile _file = file;

    [ObservableProperty]
    private bool _isLastPlayed;

    public int Key => File.Id;

    public string Code => File.EpisodeCode ?? "—";

    public string Name => Path.GetFileNameWithoutExtension(File.Name);

    public bool IsWatched => File.Watched;

    /// <summary>0–100; how far the episode has been watched.</summary>
    public double WatchedPercent => File.Watched ? 100 : File.Duration > 0 ? File.Position / File.Duration * 100 : 0;

    public string? Remaining => !File.Watched && File.Position > 0 && File.Duration > 0
        ? $"{Format.Duration(File.Duration - File.Position)} left"
        : null;

    public void Update(MediaFile file, int? lastPlayedFileId)
    {
        File = file;
        IsLastPlayed = file.Id == lastPlayedFileId;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task PlayAsync() => library.PlayAsync(itemId, new PlayItemRequest(File.Id));

    [RelayCommand]
    private Task PlayFromStartAsync() => library.PlayAsync(itemId, new PlayItemRequest(File.Id, FromStart: true));
}
