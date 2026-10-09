using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class SearchViewModel(IAgentClient agentClient, Func<string> agentUrl, LibraryViewModel library, IPosterLoader posters)
    : ObservableObject
{
    private string? _lastQuery;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingResults))]
    private ReleasesViewModel? _selected;

    public ObservableCollection<TitleResultViewModel> Results { get; } = [];

    public bool IsShowingResults => Selected is null;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;

        Selected = null;
        IsSearching = true;
        try
        {
            _lastQuery = Query.Trim();
            var result = await agentClient.SearchTitlesAsync(agentUrl(), _lastQuery);
            Error = result.Error;
            Results.Clear();
            foreach (var title in result.Value ?? [])
            {
                Results.Add(new TitleResultViewModel(this, title, posters)
                {
                    IsInLibrary = library.Items.Any(item => item.Item.TmdbId == title.TmdbId && item.Item.Kind == title.Kind),
                });
            }

            if (Error is null && Results.Count == 0)
                Error = "Nothing found. Try the original title.";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Back() => Selected = null;

    public async Task OpenAsync(TitleResultViewModel title)
    {
        var releases = new ReleasesViewModel(agentClient, agentUrl, library, title);
        Selected = releases;
        await releases.LoadAsync(_lastQuery);
    }
}
