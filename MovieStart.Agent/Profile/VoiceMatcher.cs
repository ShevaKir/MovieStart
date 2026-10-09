using System.Text.RegularExpressions;
using MovieStart.Shared.Player;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Profile;

/// <param name="AudioId">Audio track to select; null leaves the player's choice.</param>
/// <param name="SubtitleId">Subtitle track to select; null turns subtitles off.</param>
public sealed record TrackChoice(int? AudioId, int? SubtitleId);

/// <summary>Matches releases and file tracks against the viewer's <see cref="VoiceProfile"/>.</summary>
public static partial class VoiceMatcher
{
    /// <summary>Position of the best preference a release satisfies; null when it satisfies none.</summary>
    public static int? BestPreferenceIndex(VoiceProfile profile, IEnumerable<ReleaseAudio> audio)
    {
        var tracks = audio.ToList();
        for (var i = 0; i < profile.Audio.Count; i++)
        {
            var preference = profile.Audio[i];
            if (tracks.Any(track => Matches(preference, track.Language, track.Type, track.Studio)))
                return i;
        }

        return null;
    }

    /// <summary>Picks the audio track the profile likes best and the subtitles that go with it.</summary>
    public static TrackChoice Choose(VoiceProfile profile, IReadOnlyList<MediaTrack> audio, IReadOnlyList<MediaTrack> subtitles)
    {
        var classified = audio.Select(track => (Track: track, Info: Classify(track))).ToList();

        // First a full match (language, type, studio); failing that, at least the language.
        var chosen = profile.Audio
            .Select(preference => classified.FirstOrDefault(c => Matches(preference, c.Info.Language, c.Info.Type, c.Track.Title)))
            .FirstOrDefault(c => c.Track is not null);
        if (chosen.Track is null)
        {
            chosen = profile.Audio
                .Select(preference => classified.FirstOrDefault(c => c.Info.Language == preference.Language))
                .FirstOrDefault(c => c.Track is not null);
        }

        return new TrackChoice(chosen.Track?.Id, ChooseSubtitles(profile, chosen.Track is null ? null : chosen.Info, subtitles));
    }

    private static int? ChooseSubtitles(VoiceProfile profile, (string? Language, VoiceType? Type)? audio, IReadOnlyList<MediaTrack> subtitles)
    {
        var tracks = subtitles.Select(track => (Track: track, Language: NormalizeLanguage(track.Language) ?? LanguageFromTitle(track.Title)))
            .ToList();
        var understood = profile.Audio.Select(preference => preference.Language).ToHashSet();

        // Dubbed into a language the viewer understands: only forced subtitles for signs and foreign lines.
        if (audio is { Language: { } language, Type: not VoiceType.Original } && understood.Contains(language))
            return tracks.FirstOrDefault(t => t.Language == language && IsForced(t.Track.Title)).Track?.Id;

        // Original audio (or nothing matched): full subtitles in the first preferred language available.
        foreach (var subtitleLanguage in profile.SubtitleLanguages.Select(NormalizeLanguage))
        {
            var match = tracks.Where(t => t.Language == subtitleLanguage).OrderBy(t => IsForced(t.Track.Title)).FirstOrDefault();
            if (match.Track is not null)
                return match.Track.Id;
        }

        return null;
    }

    internal static (string? Language, VoiceType? Type) Classify(MediaTrack track) =>
        (NormalizeLanguage(track.Language) ?? LanguageFromTitle(track.Title), TypeFromTitle(track.Title));

    private static bool Matches(VoicePreference preference, string? language, VoiceType? type, string? studioText) =>
        language == NormalizeLanguage(preference.Language)
        && (preference.Type is null || type == preference.Type)
        && (preference.Studio is null || (studioText?.Contains(preference.Studio, StringComparison.OrdinalIgnoreCase) ?? false));

    internal static string? NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "ukr" or "uk" or "ua" or "ukrainian" => "ukr",
        "rus" or "ru" or "russian" => "rus",
        "eng" or "en" or "english" => "eng",
        null or "" => null,
        var other => other,
    };

    private static string? LanguageFromTitle(string? title)
    {
        if (title is null)
            return null;
        if (UkrainianWord().IsMatch(title))
            return "ukr";
        if (RussianWord().IsMatch(title))
            return "rus";
        return EnglishWord().IsMatch(title) ? "eng" : null;
    }

    private static VoiceType? TypeFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        if (Original().IsMatch(title))
            return VoiceType.Original;
        if (Dub().IsMatch(title))
            return VoiceType.Dub;
        if (MultiVoice().IsMatch(title))
            return VoiceType.Mvo;
        if (TwoVoice().IsMatch(title))
            return VoiceType.Dvo;
        if (Author().IsMatch(title))
            return VoiceType.Avo;
        return SingleVoice().IsMatch(title) ? VoiceType.Vo : null;
    }

    private static bool IsForced(string? title) => title is not null && Forced().IsMatch(title);

    [GeneratedRegex(@"\bOriginal\b|Оригин|Оригін", RegexOptions.IgnoreCase)]
    private static partial Regex Original();

    [GeneratedRegex(@"\bDub\b|Дубл", RegexOptions.IgnoreCase)]
    private static partial Regex Dub();

    [GeneratedRegex(@"\bMVO\b|Многогол|Багатогол", RegexOptions.IgnoreCase)]
    private static partial Regex MultiVoice();

    [GeneratedRegex(@"\bDVO\b|Двухгол|Двогол", RegexOptions.IgnoreCase)]
    private static partial Regex TwoVoice();

    [GeneratedRegex(@"\bAVO\b|Автор", RegexOptions.IgnoreCase)]
    private static partial Regex Author();

    [GeneratedRegex(@"\bVO\b|Одногол", RegexOptions.IgnoreCase)]
    private static partial Regex SingleVoice();

    [GeneratedRegex(@"forced|форс|надпис|написи", RegexOptions.IgnoreCase)]
    private static partial Regex Forced();

    [GeneratedRegex(@"\bUkr|Укр", RegexOptions.IgnoreCase)]
    private static partial Regex UkrainianWord();

    [GeneratedRegex(@"\bRus|Рус", RegexOptions.IgnoreCase)]
    private static partial Regex RussianWord();

    [GeneratedRegex(@"\bEng", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishWord();
}
