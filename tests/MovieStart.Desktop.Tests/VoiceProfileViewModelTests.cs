using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.Tests;

public class VoiceProfileViewModelTests
{
    private readonly FakeAgentClient _agent = new();
    private readonly VoiceProfileViewModel _profile;

    public VoiceProfileViewModelTests()
    {
        _profile = new VoiceProfileViewModel(_agent, () => "http://pi.local:5080");
    }

    [Fact]
    public async Task LoadsAndRoundTripsTheProfile()
    {
        _agent.Profile = new VoiceProfile([new VoicePreference("rus", VoiceType.Mvo, "HDRezka"), new VoicePreference("eng")], ["eng"]);

        await _profile.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Russian · Multi-voice · HDRezka", "English · Any · "],
            _profile.Preferences.Select(row => $"{row.Language.Label} · {row.Type.Label} · {row.Studio}"));
        Assert.Equal([false, false, true], _profile.SubtitleLanguages.Select(language => language.IsSelected));
        Assert.Equal(_agent.Profile.Audio, _profile.ToProfile().Audio);
        Assert.Equal(_agent.Profile.SubtitleLanguages, _profile.ToProfile().SubtitleLanguages);
    }

    [Fact]
    public void ReordersAddsAndRemovesPreferences()
    {
        _profile.Apply(VoiceProfile.Default);

        _profile.Preferences[1].MoveUpCommand.Execute(null);
        _profile.Preferences[3].RemoveCommand.Execute(null);
        _profile.AddCommand.Execute(null);
        _profile.Preferences[0].MoveUpCommand.Execute(null);

        Assert.Equal(
            [new VoicePreference("rus", VoiceType.Dub), new VoicePreference("ukr", VoiceType.Dub), new VoicePreference("rus", VoiceType.Mvo), new VoicePreference("ukr")],
            _profile.ToProfile().Audio);
    }

    [Fact]
    public void BlankStudioMeansAnyStudio()
    {
        _profile.Apply(new VoiceProfile([new VoicePreference("rus", VoiceType.Mvo, "LostFilm")], []));

        _profile.Preferences[0].Studio = "  ";

        Assert.Null(_profile.ToProfile().Audio[0].Studio);
    }

    [Fact]
    public async Task SavesToTheAgent()
    {
        _profile.Apply(VoiceProfile.Default);
        _profile.SubtitleLanguages[2].IsSelected = false;

        await _profile.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["ukr", "rus"], _agent.SavedProfile!.SubtitleLanguages);
        Assert.Equal("Saved. New searches and playback use it.", _profile.Status);
    }

    [Fact]
    public async Task ShowsSaveErrors()
    {
        _agent.Result = AgentResult.Failure("Every audio preference needs a language.");

        await _profile.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Every audio preference needs a language.", _profile.Error);
        Assert.Null(_profile.Status);
    }
}
