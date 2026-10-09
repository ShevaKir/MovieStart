using CommunityToolkit.Mvvm.ComponentModel;
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

    public void Update(DownloadFile file)
    {
        _applying = true;
        try
        {
            File = file;
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
            _ = download.SendSelectionAsync();
    }
}
