using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Library;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.ViewModels;

public partial class TitleResultViewModel : ObservableObject
{
    private readonly SearchViewModel _search;

    [ObservableProperty]
    private Bitmap? _poster;

    [ObservableProperty]
    private bool _isInLibrary;

    public TitleResultViewModel(SearchViewModel search, TitleResult title, IPosterLoader posters)
    {
        _search = search;
        Title = title;
        if (title.PosterUrl is { } url)
            _ = LoadPosterAsync(posters, url);
    }

    public TitleResult Title { get; }

    public string Name => Title.Title;

    public string Details => string.Join(" · ", new[]
    {
        Title.Year?.ToString(),
        Title.Kind == MediaKind.Series ? "Series" : "Movie",
        Title.OriginalTitle is { } original && original != Title.Title ? original : null,
    }.Where(part => part is not null));

    public string? Rating => Title.Rating?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    public string Initials => string.Concat(Title.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    [RelayCommand]
    private Task OpenAsync() => _search.OpenAsync(this);

    private async Task LoadPosterAsync(IPosterLoader posters, string url) => Poster = await posters.LoadAsync(url);
}
