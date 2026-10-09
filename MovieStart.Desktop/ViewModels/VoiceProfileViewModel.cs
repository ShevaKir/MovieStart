using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Editor for the voice-over profile kept on the agent.</summary>
public partial class VoiceProfileViewModel(IAgentClient agentClient, Func<string> agentUrl) : ObservableObject
{
    public static IReadOnlyList<Choice<string>> Languages { get; } =
        [new("ukr", "Ukrainian"), new("rus", "Russian"), new("eng", "English")];

    public static IReadOnlyList<Choice<VoiceType?>> Types { get; } =
    [
        new(null, "Any"),
        new(VoiceType.Dub, "Dub"),
        new(VoiceType.Mvo, "Multi-voice"),
        new(VoiceType.Dvo, "Two-voice"),
        new(VoiceType.Avo, "Author"),
        new(VoiceType.Vo, "Single voice"),
        new(VoiceType.Original, "Original"),
    ];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _error;

    public ObservableCollection<VoicePreferenceRowViewModel> Preferences { get; } = [];

    /// <summary>Subtitle languages in a fixed order (Ukrainian, Russian, English).</summary>
    public IReadOnlyList<SubtitleLanguageViewModel> SubtitleLanguages { get; } =
        Languages.Select(language => new SubtitleLanguageViewModel(language.Value, language.Label)).ToList();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await agentClient.GetVoiceProfileAsync(agentUrl(), cancellationToken);
        Error = result.Error;
        if (result.Value is { } profile)
            Apply(profile);
    }

    public void Apply(VoiceProfile profile)
    {
        Preferences.Clear();
        foreach (var preference in profile.Audio)
            Preferences.Add(new VoicePreferenceRowViewModel(this, preference));
        foreach (var language in SubtitleLanguages)
            language.IsSelected = profile.SubtitleLanguages.Contains(language.Code);
        Status = null;
    }

    public VoiceProfile ToProfile() => new(
        Preferences.Select(row => row.ToPreference()).ToList(),
        SubtitleLanguages.Where(language => language.IsSelected).Select(language => language.Code).ToList());

    [RelayCommand]
    private void Add() => Preferences.Add(new VoicePreferenceRowViewModel(this, new VoicePreference("ukr")));

    [RelayCommand]
    private void ResetToDefault() => Apply(VoiceProfile.Default);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var result = await agentClient.SaveVoiceProfileAsync(agentUrl(), ToProfile());
        Error = result.Error;
        Status = result.IsSuccess ? "Saved. New searches and playback use it." : null;
    }

    internal void Move(VoicePreferenceRowViewModel row, int offset)
    {
        var index = Preferences.IndexOf(row);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < Preferences.Count)
            Preferences.Move(index, target);
    }

    internal void Remove(VoicePreferenceRowViewModel row) => Preferences.Remove(row);
}

public partial class VoicePreferenceRowViewModel : ObservableObject
{
    private readonly VoiceProfileViewModel _profile;

    [ObservableProperty]
    private Choice<string> _language;

    [ObservableProperty]
    private Choice<VoiceType?> _type;

    [ObservableProperty]
    private string _studio;

    public VoicePreferenceRowViewModel(VoiceProfileViewModel profile, VoicePreference preference)
    {
        _profile = profile;
        _language = VoiceProfileViewModel.Languages.FirstOrDefault(l => l.Value == preference.Language)
            ?? new Choice<string>(preference.Language, preference.Language.ToUpperInvariant());
        _type = VoiceProfileViewModel.Types.First(t => t.Value == preference.Type);
        _studio = preference.Studio ?? string.Empty;
    }

    public IReadOnlyList<Choice<string>> Languages => VoiceProfileViewModel.Languages;

    public IReadOnlyList<Choice<VoiceType?>> Types => VoiceProfileViewModel.Types;

    public VoicePreference ToPreference() =>
        new(Language.Value, Type.Value, string.IsNullOrWhiteSpace(Studio) ? null : Studio.Trim());

    [RelayCommand]
    private void MoveUp() => _profile.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _profile.Move(this, 1);

    [RelayCommand]
    private void Remove() => _profile.Remove(this);
}

public partial class SubtitleLanguageViewModel(string code, string label) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Code { get; } = code;

    public string Label { get; } = label;
}
