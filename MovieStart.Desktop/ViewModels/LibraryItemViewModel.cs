using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class LibraryItemViewModel : ObservableObject, IKeyed<Guid>
{
    private readonly LibraryViewModel _library;

    [ObservableProperty]
    private LibraryItem _item;

    [ObservableProperty]
    private Bitmap? _poster;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private bool _isExpanded;

    public LibraryItemViewModel(LibraryViewModel library, LibraryItem item)
    {
        _library = library;
        _item = item;
        Update(item);
        if (item.PosterUrl is not null && library.Posters is { } posters && library.PosterUrl(item.Id) is { } url)
            _ = LoadPosterAsync(posters, url);
    }

    public Guid Id => Item.Id;

    public Guid Key => Item.Id;

    public string Title => Item.Title;

    public string Subtitle => string.Join(" · ", new[]
    {
        Item.Year?.ToString(),
        Item.Kind == MediaKind.Series ? "Series" : "Movie",
        Item.Files.Count > 0 ? Format.Size(Item.Files.Sum(file => file.SizeBytes)) : null,
    }.Where(part => part is not null));

    public bool IsSeries => Item.Kind == MediaKind.Series;

    /// <summary>First letters of the title; shown until the poster loads, or when there is none.</summary>
    public string Initials => string.Concat(Item.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];

    public ObservableCollection<EpisodeViewModel> Episodes { get; } = [];

    public bool HasActiveDownloads => Downloads.Any(download => download.IsActive);

    public bool CanPlay => Item.Files.Count > 0;

    public string PlayLabel
    {
        get
        {
            if (Item.ChooseFileToContinue() is not { } next)
                return "Watch";

            var episode = next.EpisodeCode is { } code ? $" {code}" : string.Empty;
            if (next.Id == Item.LastPlayedFileId)
            {
                return next.Position > 0 ? $"Continue{episode} from {Format.Duration(next.Position)}" : $"Continue{episode}";
            }

            // The last played episode is finished, so another one is offered.
            return Item.LastPlayedFileId is not null && IsSeries ? $"Next{episode}" : $"Watch{episode}";
        }
    }

    /// <summary>The file being continued was stopped partway, so it can also be restarted.</summary>
    public bool CanStartOver => Item.ChooseFileToContinue() is { Position: > 0 } next && next.Id == Item.LastPlayedFileId;

    public string? EpisodesSummary => IsSeries && Item.Files.Count > 0
        ? $"{Item.Files.Count} episodes · {Item.Files.Count(file => file.Watched)} watched"
        : null;

    public void Update(LibraryItem item)
    {
        Item = item;
        CollectionSync.Sync(Downloads, item.Downloads, d => d.Id, d => new DownloadViewModel(_library, item.Id, d), (vm, d) => vm.Update(d));
        foreach (var download in Downloads)
            download.CanDelete = item.Kind == MediaKind.Series || Downloads.Count > 1;
        CollectionSync.Sync(
            Episodes,
            item.Kind == MediaKind.Series ? item.Files : [],
            f => f.Id,
            f => new EpisodeViewModel(_library, item.Id, f),
            (vm, f) => vm.Update(f, item.LastPlayedFileId));
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private Task PlayAsync() => _library.PlayAsync(Id, new PlayItemRequest());

    [RelayCommand]
    private Task StartOverAsync() =>
        Item.ChooseFileToContinue() is { } next ? _library.PlayAsync(Id, new PlayItemRequest(next.Id, FromStart: true)) : Task.CompletedTask;

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        return _library.DeleteItemAsync(Id);
    }

    [RelayCommand]
    private void ToggleEpisodes() => IsExpanded = !IsExpanded;

    private async Task LoadPosterAsync(IPosterLoader posters, string url) => Poster = await posters.LoadAsync(url);
}
