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

    public string? EpisodesSummary
    {
        get
        {
            if (!IsSeries || Episodes.Count == 0)
                return null;

            var downloading = Episodes.Count(episode => episode.IsFetching);
            var notDownloaded = Episodes.Count(episode => episode.CanFetch);
            return string.Join(" · ", new[]
            {
                Item.Files.Count == 1 ? "1 episode" : $"{Item.Files.Count} episodes",
                $"{Item.Files.Count(file => file.Watched)} watched",
                downloading > 0 ? $"{downloading} downloading" : null,
                notDownloaded > 0 ? $"{notDownloaded} not downloaded" : null,
            }.Where(part => part is not null));
        }
    }

    public void Update(LibraryItem item)
    {
        Item = item;
        CollectionSync.Sync(Downloads, item.Downloads, d => d.Id, d => new DownloadViewModel(_library, item.Id, d), (vm, d) => vm.Update(d));
        foreach (var download in Downloads)
        {
            download.CanDelete = item.Kind == MediaKind.Series || Downloads.Count > 1;
            download.IsSeries = item.Kind == MediaKind.Series;
        }
        CollectionSync.Sync(
            Episodes,
            item.Kind == MediaKind.Series ? EpisodeEntries(item) : [],
            entry => entry.Key,
            entry => new EpisodeViewModel(_library, item.Id, entry),
            (vm, entry) => vm.Update(entry, item.LastPlayedFileId));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Episodes on the Pi plus the ones their season torrents can still fetch, in season and episode order.
    /// An episode already on the Pi from another download is not offered again.
    /// </summary>
    private static List<EpisodeEntry> EpisodeEntries(LibraryItem item)
    {
        var names = item.Files.Select(file => file.Name.Replace('\\', '/')).ToHashSet();
        var codes = item.Files.Select(file => file.EpisodeCode).OfType<string>().ToHashSet();
        var missing = item.Downloads
            .Where(download => download.Status != DownloadStatus.Error && download.Files is not null)
            .SelectMany(download => download.Files!
                .Where(file => file.IsVideo
                    && !names.Contains($"{download.Id:N}/{file.Name}")
                    && (file.EpisodeCode is null || !codes.Contains(file.EpisodeCode)))
                .Select(file => new EpisodeEntry(null, download, file)));

        return item.Files.Select(file => new EpisodeEntry(file))
            .Concat(missing)
            .OrderBy(entry => entry.Season ?? int.MaxValue)
            .ThenBy(entry => entry.Episode ?? int.MaxValue)
            .ToList();
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
