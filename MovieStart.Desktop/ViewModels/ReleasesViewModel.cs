using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Library;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.ViewModels;

/// <summary>Releases of one title, with language, voice-over and season filters.</summary>
public partial class ReleasesViewModel : ObservableObject
{
    private readonly IAgentClient _agentClient;
    private readonly Func<string> _agentUrl;
    private readonly LibraryViewModel _library;
    private List<ReleaseRowViewModel> _all = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string _summary = string.Empty;

    public ReleasesViewModel(IAgentClient agentClient, Func<string> agentUrl, LibraryViewModel library, TitleResultViewModel title)
    {
        _agentClient = agentClient;
        _agentUrl = agentUrl;
        _library = library;
        Title = title;

        Languages = [Chip("ukr", "UKR"), Chip("rus", "RUS"), Chip("eng", "ENG")];
        Voices =
        [
            Chip(VoiceType.Dub, "Dub"),
            Chip(VoiceType.Mvo, "Multi-voice"),
            Chip(VoiceType.Avo, "Author"),
            Chip(VoiceType.Original, "Original"),
        ];
    }

    public TitleResultViewModel Title { get; }

    public bool IsSeries => Title.Title.Kind == MediaKind.Series;

    public IReadOnlyList<FilterChip<string>> Languages { get; }

    public IReadOnlyList<FilterChip<VoiceType>> Voices { get; }

    /// <summary>Seasons found in the releases; empty for movies.</summary>
    public ObservableCollection<FilterChip<int>> Seasons { get; } = [];

    public bool HasSeasons => Seasons.Count > 0;

    public ObservableCollection<ReleaseRowViewModel> Releases { get; } = [];

    public async Task LoadAsync(string? query, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Error = null;
        try
        {
            var result = await _agentClient.SearchReleasesAsync(_agentUrl(), Title.Title, query, cancellationToken);
            Error = result.Error;
            _all = (result.Value ?? []).Select(release => new ReleaseRowViewModel(this, release)).ToList();

            Seasons.Clear();
            foreach (var season in _all.Select(row => row.Release.SeasonFrom).OfType<int>().Distinct().Order())
                Seasons.Add(Chip(season, $"Season {season}"));
            OnPropertyChanged(nameof(HasSeasons));

            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> DownloadAsync(ReleaseInfo release)
    {
        var result = await _agentClient.AddToLibraryAsync(_agentUrl(), release.ToAddRequest(Title.Title));
        Error = result.Error;
        if (result.IsSuccess)
        {
            Title.IsInLibrary = true;
            await _library.RefreshAsync();
        }

        return result.IsSuccess;
    }

    internal void ApplyFilters()
    {
        var languages = Languages.Where(c => c.IsSelected).Select(c => c.Value).ToHashSet();
        var voices = Voices.Where(c => c.IsSelected).Select(c => c.Value).ToHashSet();
        var seasons = Seasons.Where(c => c.IsSelected).Select(c => c.Value).ToHashSet();

        // A track matches when it fits every dimension the user picked, e.g. "UKR" + "Dub" = Ukrainian dub.
        bool Matches(ReleaseAudio track) =>
            (languages.Count == 0 || languages.Contains(track.Language))
            && (voices.Count == 0 || voices.Contains(track.Type));

        var filtering = languages.Count > 0 || voices.Count > 0;
        var visible = _all
            .Where(row => !filtering || row.Release.Audio.Any(Matches))
            .Where(row => seasons.Count == 0 || Covers(row.Release, seasons))
            .ToList();

        Releases.Clear();
        foreach (var row in visible)
        {
            row.Highlight(track => filtering && Matches(track));
            row.IsBest = false;
            Releases.Add(row);
        }

        // The agent sorts by quality, so the first remaining release is the best fit.
        if (Releases.Count > 0)
            Releases[0].IsBest = true;

        Summary = _all.Count == 0 ? string.Empty : $"{visible.Count} of {_all.Count}";
    }

    private static bool Covers(ReleaseInfo release, HashSet<int> seasons) =>
        release.SeasonFrom is { } from && seasons.Any(season => season >= from && season <= (release.SeasonTo ?? from));

    private FilterChip<T> Chip<T>(T value, string label) => new(value, label, ApplyFilters);
}
