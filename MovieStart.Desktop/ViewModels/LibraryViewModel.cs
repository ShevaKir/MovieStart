using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Library;

namespace MovieStart.Desktop.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IAgentClient _agentClient;
    private readonly Func<string> _agentUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageText), nameof(StorageUsedPercent))]
    private StorageInfo? _storage;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isAddFormOpen;

    [ObservableProperty]
    private bool _newIsSeries;

    [ObservableProperty]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private string _newSource = string.Empty;

    [ObservableProperty]
    private string _newSeason = string.Empty;

    [ObservableProperty]
    private string _newEpisode = string.Empty;

    public LibraryViewModel(IAgentClient agentClient, Func<string> agentUrl, IPosterLoader? posters = null)
    {
        _agentClient = agentClient;
        _agentUrl = agentUrl;
        Posters = posters;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<LibraryItemViewModel> Items { get; } = [];

    /// <summary>Null in tests; the cards then keep their initials.</summary>
    internal IPosterLoader? Posters { get; }

    public bool IsEmpty => Items.Count == 0;

    public string StorageText => Storage is { } s ? $"{Format.Size(s.FreeBytes)} free of {Format.Size(s.TotalBytes)}" : "Disk: unknown";

    public double StorageUsedPercent => Storage is { TotalBytes: > 0 } s ? 100d * (s.TotalBytes - s.FreeBytes) / s.TotalBytes : 0;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var url = _agentUrl();
        var items = await _agentClient.GetLibraryAsync(url, cancellationToken);
        if (items is not null)
            CollectionSync.Sync(Items, items, item => item.Id, item => new LibraryItemViewModel(this, item), (vm, item) => vm.Update(item));

        Storage = await _agentClient.GetStorageAsync(url, cancellationToken) ?? Storage;
    }

    public async Task RunPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RefreshAsync(cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    public Task PlayAsync(Guid itemId, PlayItemRequest request) =>
        RunAsync(async () => await _agentClient.PlayItemAsync(_agentUrl(), itemId, request));

    public Task DeleteItemAsync(Guid itemId) =>
        RunAsync(() => _agentClient.DeleteItemAsync(_agentUrl(), itemId));

    public Task DeleteDownloadAsync(Guid itemId, Guid downloadId) =>
        RunAsync(() => _agentClient.DeleteDownloadAsync(_agentUrl(), itemId, downloadId));

    public Task PauseDownloadAsync(Guid itemId, Guid downloadId) =>
        RunAsync(() => _agentClient.PauseDownloadAsync(_agentUrl(), itemId, downloadId));

    public Task ResumeDownloadAsync(Guid itemId, Guid downloadId) =>
        RunAsync(() => _agentClient.ResumeDownloadAsync(_agentUrl(), itemId, downloadId));

    [RelayCommand]
    private void ToggleAddForm() => IsAddFormOpen = !IsAddFormOpen;

    // Temporary until search exists: add a download from a magnet link by hand.
    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTitle) || string.IsNullOrWhiteSpace(NewSource))
        {
            Error = "Title and magnet link are required.";
            return;
        }

        if (!TryParseNumber(NewSeason, out var season) || !TryParseNumber(NewEpisode, out var episode))
        {
            Error = "Season and episode must be numbers.";
            return;
        }

        var request = new AddToLibraryRequest(
            NewIsSeries ? MediaKind.Series : MediaKind.Movie,
            NewTitle.Trim(),
            NewSource.Trim(),
            Season: NewIsSeries ? season : null,
            Episode: NewIsSeries ? episode : null);

        if (await RunAsync(() => _agentClient.AddToLibraryAsync(_agentUrl(), request)))
        {
            NewTitle = NewSource = NewSeason = NewEpisode = string.Empty;
            IsAddFormOpen = false;
        }
    }

    private async Task<bool> RunAsync(Func<Task<AgentResult>> action)
    {
        var result = await action();
        Error = result.Error;
        if (result.IsSuccess)
            await RefreshAsync();
        return result.IsSuccess;
    }

    private static bool TryParseNumber(string text, out int? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!int.TryParse(text.Trim(), out var value) || value < 0)
            return false;
        number = value;
        return true;
    }
}
