using CommunityToolkit.Mvvm.ComponentModel;
using MovieStart.Client;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

/// <summary>One episode of a running download, with a checkbox for whether to fetch it.</summary>
public partial class DownloadFileViewModel(DownloadViewModel download, DownloadFile file) : ObservableObject, IKeyed<int>
{
    private bool _applying;

    [ObservableProperty]
    private DownloadFile _file = file;

    [ObservableProperty]
    private bool _isWanted = file.Wanted;

    public int Key => File.Index;

    public string Label => File.EpisodeCode ?? Path.GetFileNameWithoutExtension(File.Name);

    public string Size => Format.Size(File.SizeBytes);

    /// <param name="keepTick">The viewer is still choosing; the tick has not been sent yet.</param>
    public void Update(DownloadFile file, bool keepTick = false)
    {
        _applying = true;
        try
        {
            File = file;
            if (!keepTick)
                IsWanted = file.Wanted;
            OnPropertyChanged(string.Empty);
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnIsWantedChanged(bool value)
    {
        if (!_applying)
            download.OnEpisodeTicked();
    }
}
