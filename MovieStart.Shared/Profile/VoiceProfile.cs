using MovieStart.Shared.Search;

namespace MovieStart.Shared.Profile;

/// <summary>One acceptable audio choice, e.g. "Ukrainian dub" or "Russian multi-voice by HDRezka".</summary>
/// <param name="Language">ISO 639-2: "ukr", "rus", "eng".</param>
/// <param name="Type">Null accepts any voice-over type.</param>
/// <param name="Studio">Null accepts any studio.</param>
public sealed record VoicePreference(string Language, VoiceType? Type = null, string? Studio = null);

/// <summary>How the viewer likes to watch: used to rank releases and to pick tracks on playback.</summary>
/// <param name="Audio">Preferences, best first.</param>
/// <param name="SubtitleLanguages">Subtitle languages to turn on when the audio is in another language, best first.</param>
public sealed record VoiceProfile(IReadOnlyList<VoicePreference> Audio, IReadOnlyList<string> SubtitleLanguages)
{
    public static VoiceProfile Default { get; } = new(
        [
            new VoicePreference("ukr", VoiceType.Dub),
            new VoicePreference("rus", VoiceType.Dub),
            new VoicePreference("rus", VoiceType.Mvo),
            new VoicePreference("eng", VoiceType.Original),
        ],
        ["ukr", "rus", "eng"]);
}
