using MovieStart.Agent.Profile;
using MovieStart.Shared.Player;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Tests;

public class VoiceMatcherTests
{
    private static readonly VoiceProfile Profile = VoiceProfile.Default;

    [Fact]
    public void PrefersTheFirstPreferenceThatMatches()
    {
        MediaTrack[] audio = [new(1, "rus", "Дубляж"), new(2, "ukr", "Дубляж, Le Doyen"), new(3, "eng", null)];

        Assert.Equal(2, VoiceMatcher.Choose(Profile, audio, []).AudioId);
    }

    [Fact]
    public void ReadsTheVoiceTypeFromTheTrackTitle()
    {
        MediaTrack[] audio = [new(1, "rus", "MVO, HDRezka"), new(2, "rus", "Дублированный, Blu-ray CEE")];

        // Russian dub beats Russian multi-voice in the default profile.
        Assert.Equal(2, VoiceMatcher.Choose(Profile, audio, []).AudioId);
    }

    [Fact]
    public void FallsBackToLanguageWhenTypeIsUnknown()
    {
        MediaTrack[] audio = [new(1, "eng", null), new(2, "rus", null)];

        Assert.Equal(2, VoiceMatcher.Choose(Profile, audio, []).AudioId);
    }

    [Fact]
    public void LanguageCanComeFromTheTitle()
    {
        MediaTrack[] audio = [new(1, null, "Original"), new(2, null, "Укр. дубляж")];

        Assert.Equal(2, VoiceMatcher.Choose(Profile, audio, []).AudioId);
    }

    [Fact]
    public void StudioMustMatchWhenTheProfileNamesOne()
    {
        var profile = new VoiceProfile([new VoicePreference("rus", VoiceType.Mvo, "HDRezka")], []);
        MediaTrack[] audio = [new(1, "rus", "MVO, LostFilm"), new(2, "rus", "MVO, HDRezka Studio")];

        Assert.Equal(2, VoiceMatcher.Choose(profile, audio, []).AudioId);
    }

    [Fact]
    public void DubbedAudioGetsOnlyForcedSubtitlesInItsLanguage()
    {
        MediaTrack[] audio = [new(1, "ukr", "Дубляж")];
        MediaTrack[] subtitles = [new(1, "ukr", "Повні"), new(2, "ukr", "Forced")];

        Assert.Equal(new TrackChoice(1, 2), VoiceMatcher.Choose(Profile, audio, subtitles));
    }

    [Fact]
    public void DubbedAudioWithoutForcedSubtitlesTurnsThemOff()
    {
        MediaTrack[] audio = [new(1, "ukr", "Дубляж")];
        MediaTrack[] subtitles = [new(1, "ukr", null), new(2, "eng", null)];

        Assert.Equal(new TrackChoice(1, null), VoiceMatcher.Choose(Profile, audio, subtitles));
    }

    [Fact]
    public void OriginalAudioGetsFullSubtitlesInThePreferredLanguage()
    {
        MediaTrack[] audio = [new(1, "eng", "Original")];
        MediaTrack[] subtitles = [new(1, "eng", null), new(2, "rus", "Forced"), new(3, "rus", "Full")];

        Assert.Equal(new TrackChoice(1, 3), VoiceMatcher.Choose(Profile, audio, subtitles));
    }

    [Fact]
    public void NothingMatchingLeavesAudioToThePlayer()
    {
        MediaTrack[] audio = [new(1, "jpn", null)];
        MediaTrack[] subtitles = [new(1, "eng", null)];

        Assert.Equal(new TrackChoice(null, 1), VoiceMatcher.Choose(Profile, audio, subtitles));
    }

    [Fact]
    public void RanksReleasesByTheBestPreference()
    {
        Assert.Equal(0, VoiceMatcher.BestPreferenceIndex(Profile, [new ReleaseAudio("rus", VoiceType.Dub), new ReleaseAudio("ukr", VoiceType.Dub)]));
        Assert.Equal(2, VoiceMatcher.BestPreferenceIndex(Profile, [new ReleaseAudio("rus", VoiceType.Mvo, "LostFilm")]));
        Assert.Null(VoiceMatcher.BestPreferenceIndex(Profile, [new ReleaseAudio("rus", VoiceType.Avo, "Гаврилов")]));
    }
}
