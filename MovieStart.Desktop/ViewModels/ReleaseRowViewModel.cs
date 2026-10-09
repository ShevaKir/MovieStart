using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.ViewModels;

/// <param name="Text">"UKR Dub", "RUS MVO HDRezka", "ENG Original".</param>
public sealed record AudioChip(string Text, bool IsPreferred);

public partial class ReleaseRowViewModel(ReleasesViewModel releases, ReleaseInfo release) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadLabel))]
    private bool _isAdded;

    [ObservableProperty]
    private bool _isBest;

    [ObservableProperty]
    private IReadOnlyList<AudioChip> _audio = [];

    public ReleaseInfo Release { get; } = release;

    public string Quality => string.Join(" · ", new[]
    {
        SourceName(Release.Source),
        Release.Codec is { } codec ? Release.Is10Bit ? $"{codec} 10-bit" : codec : null,
        Release.Container,
        Release.Indexer,
    }.Where(part => !string.IsNullOrEmpty(part)));

    public string? Episodes => Release switch
    {
        { SeasonFrom: { } from, SeasonTo: { } to } when to != from => $"Seasons {from}–{to}",
        { Season: { } s, Episode: { } e } => $"S{s:00}E{e:00}",
        { Season: { } s, EpisodeFrom: { } from, EpisodeTo: { } to } => $"Season {s} · episodes {from}–{to}",
        { Season: { } s } => $"Season {s}",
        _ => null,
    };

    public bool IsRemux => Release.Source == ReleaseSource.Remux;

    public string Size => Release.SizeBytes > 0 ? Format.Size(Release.SizeBytes) : "—";

    public string Seeders => Release.Seeders.ToString();

    public string? Subtitles => Release.Subtitles.Count > 0 ? "Sub " + string.Join(", ", Release.Subtitles.Select(s => s.ToUpperInvariant())) : null;

    public string? Warning => Release.Warnings.Count > 0 ? string.Join(" ", Release.Warnings) : null;

    public string DownloadLabel => IsAdded ? "Added" : "Download to Pi";

    public void Highlight(Func<ReleaseAudio, bool> isPreferred) =>
        Audio = Release.Audio.Select(track => new AudioChip(Describe(track), isPreferred(track))).ToList();

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (!IsAdded)
            IsAdded = await releases.DownloadAsync(Release);
    }

    private static string Describe(ReleaseAudio track) =>
        string.Join(" ", new[] { track.Language.ToUpperInvariant(), VoiceName(track.Type), track.Studio }.Where(part => part is not null));

    internal static string VoiceName(VoiceType type) => type switch
    {
        VoiceType.Dub => "Dub",
        VoiceType.Mvo => "MVO",
        VoiceType.Dvo => "DVO",
        VoiceType.Avo => "AVO",
        VoiceType.Vo => "VO",
        _ => "Original",
    };

    private static string? SourceName(ReleaseSource source) => source switch
    {
        ReleaseSource.Remux => "BDRemux",
        ReleaseSource.BdRip => "BDRip",
        ReleaseSource.WebDl => "WEB-DL",
        ReleaseSource.WebRip => "WEBRip",
        ReleaseSource.HdTv => "HDTV",
        ReleaseSource.HdRip => "HDRip",
        ReleaseSource.DvdRip => "DVDRip",
        _ => null,
    };
}
