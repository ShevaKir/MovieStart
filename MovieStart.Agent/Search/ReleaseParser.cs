using System.Text.RegularExpressions;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Search;

/// <summary>
/// Reads quality, audio tracks and episode ranges from release titles.
/// Titles are free text, so this is a best-effort heuristic tuned to rutracker, kinozal, rutor, Toloka and scene names.
/// </summary>
public static partial class ReleaseParser
{
    /// <summary>Studios whose language is not written in the title.</summary>
    private static readonly Dictionary<string, string> StudioLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HDRezka"] = "rus",
        ["LostFilm"] = "rus",
        ["Jaskier"] = "rus",
        ["Red Head Sound"] = "rus",
        ["NewStudio"] = "rus",
        ["TVShows"] = "rus",
        ["AlexFilm"] = "rus",
        ["BaibaKo"] = "rus",
        ["Кубик в Кубе"] = "rus",
        ["Пифагор"] = "rus",
        ["Le Doyen"] = "ukr",
        ["DniproFilm"] = "ukr",
        ["Postmodern"] = "ukr",
        ["Так Треба Продакшн"] = "ukr",
        ["Цікава Ідея"] = "ukr",
        ["UATeam"] = "ukr",
        ["Hurtom"] = "ukr",
        ["Amanogawa"] = "ukr",
    };

    private static readonly Dictionary<string, string> LanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rus"] = "rus",
        ["ru"] = "rus",
        ["рус"] = "rus",
        ["ukr"] = "ukr",
        ["ua"] = "ukr",
        ["укр"] = "ukr",
        ["eng"] = "eng",
        ["en"] = "eng",
        ["англ"] = "eng",
    };

    /// <param name="defaultLanguage">Language of voice-overs that do not name one; "ukr" for Ukrainian trackers.</param>
    public static ParsedRelease Parse(string title, string defaultLanguage = "rus")
    {
        var (seasonFrom, seasonTo, episodeFrom, episodeTo) = ParseEpisodes(title);
        var subtitles = Subtitles().Match(title);

        // Languages after "Sub" are subtitles, not audio.
        var audioText = subtitles.Success ? title.Remove(subtitles.Index, subtitles.Length) : title;

        return new ParsedRelease(
            ParseResolution(title),
            ParseSource(title),
            ParseCodec(title),
            TenBit().IsMatch(title),
            Container().Match(title) is { Success: true } container ? container.Value.ToUpperInvariant() : null,
            ParseAudio(audioText, defaultLanguage),
            ParseSubtitles(subtitles),
            seasonFrom,
            seasonTo,
            episodeFrom,
            episodeTo);
    }

    private static int? ParseResolution(string title)
    {
        if (Resolution().Match(title) is { Success: true } match)
            return int.Parse(match.Groups["r"].Value);
        return UltraHd().IsMatch(title) ? 2160 : null;
    }

    private static ReleaseSource ParseSource(string title)
    {
        // Order matters: "WEB-DLRip" is a rip, "BDRemux" is not a BDRip.
        if (Remux().IsMatch(title))
            return ReleaseSource.Remux;
        if (BdRip().IsMatch(title))
            return ReleaseSource.BdRip;
        if (WebRip().IsMatch(title))
            return ReleaseSource.WebRip;
        if (WebDl().IsMatch(title))
            return ReleaseSource.WebDl;
        if (HdTv().IsMatch(title))
            return ReleaseSource.HdTv;
        if (HdRip().IsMatch(title))
            return ReleaseSource.HdRip;
        if (DvdRip().IsMatch(title))
            return ReleaseSource.DvdRip;
        if (Cam().IsMatch(title))
            return ReleaseSource.Cam;
        return ReleaseSource.Unknown;
    }

    private static string? ParseCodec(string title)
    {
        if (Hevc().IsMatch(title))
            return "x265";
        if (Avc().IsMatch(title))
            return "x264";
        return Av1().IsMatch(title) ? "AV1" : null;
    }

    private static IReadOnlyList<ReleaseAudio> ParseAudio(string title, string defaultLanguage)
    {
        var tracks = new List<ReleaseAudio>();

        // rutracker: "Dub + MVO (HDRezka Studio) + AVO (Гаврилов) + Ukr (Dub) + Original Eng"
        foreach (Match match in VoiceToken().Matches(title))
        {
            // "Ukr (Dub)": the type belongs to the Ukrainian track handled below.
            if (IsInsideParentheses(title, match.Index))
                continue;

            var type = ToVoiceType(match.Groups["type"].Value);
            var details = match.Groups["details"].Success ? match.Groups["details"].Value.Trim() : null;
            var (language, studio) = SplitDetails(details, defaultLanguage);
            tracks.Add(new ReleaseAudio(language, type, studio));
        }

        // "Ukr (Dub)", "Ukr (Le Doyen)"
        foreach (Match match in UkrainianToken().Matches(title))
        {
            // "MVO (Ukr)": already counted as the language of that voice-over.
            if (IsInsideParentheses(title, match.Index))
                continue;

            var details = match.Groups["details"].Success ? match.Groups["details"].Value.Trim() : null;
            var type = details is not null && VoiceTypeName().Match(details) is { Success: true } t ? ToVoiceType(t.Value) : VoiceType.Dub;
            var studio = details is not null && !VoiceTypeName().IsMatch(details) ? details : null;
            tracks.Add(new ReleaseAudio("ukr", type, studio));
        }

        // "Original Eng", "Original" (English by default)
        foreach (Match match in OriginalToken().Matches(title))
        {
            var language = match.Groups["lang"].Success ? ToLanguage(match.Groups["lang"].Value) ?? "eng" : "eng";
            tracks.Add(new ReleaseAudio(language, VoiceType.Original));
        }

        // kinozal / rutor: "| D, P, A |" — D dub, P multi-voice, P2 two-voice, P1/A author, L amateur.
        foreach (Match match in KinozalCodes().Matches(title))
        {
            foreach (var code in match.Groups["codes"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var type = code.ToUpperInvariant() switch
                {
                    "D" => VoiceType.Dub,
                    "P" or "L" => VoiceType.Mvo,
                    "P2" or "L2" => VoiceType.Dvo,
                    "P1" or "L1" or "A" => VoiceType.Avo,
                    _ => (VoiceType?)null,
                };
                if (type is { } voice)
                    tracks.Add(new ReleaseAudio(defaultLanguage, voice));
            }
        }

        // Toloka: "Ukr/Eng" with no voice types at all.
        if (tracks.Count == 0 && LanguageList().Match(title) is { Success: true } list)
        {
            foreach (var language in list.Value.Split('/').Select(ToLanguage).OfType<string>())
                tracks.Add(new ReleaseAudio(language, language == "eng" ? VoiceType.Original : VoiceType.Dub));
        }

        return tracks.Distinct().ToList();
    }

    private static (string Language, string? Studio) SplitDetails(string? details, string defaultLanguage)
    {
        if (string.IsNullOrEmpty(details))
            return (defaultLanguage, null);

        // "(Ukr)" or "(Ukr, Le Doyen)": a language, optionally with a studio.
        var parts = details.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var language = parts.Select(ToLanguage).OfType<string>().FirstOrDefault();
        var studio = parts.FirstOrDefault(part => ToLanguage(part) is null);
        if (studio is not null)
            studio = StudioSuffix().Replace(studio, string.Empty).Trim();

        language ??= studio is not null ? StudioLanguages.GetValueOrDefault(studio) : null;
        return (language ?? defaultLanguage, string.IsNullOrEmpty(studio) ? null : studio);
    }

    private static bool IsInsideParentheses(string text, int index)
    {
        var depth = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && depth > 0)
                depth--;
        }

        return depth > 0;
    }

    private static IReadOnlyList<string> ParseSubtitles(Match match)
    {
        if (!match.Success)
            return [];

        return match.Groups["langs"].Value
            .Split([',', '/', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ToLanguage)
            .OfType<string>()
            .Distinct()
            .ToList();
    }

    private static (int? SeasonFrom, int? SeasonTo, int? EpisodeFrom, int? EpisodeTo) ParseEpisodes(string title)
    {
        // rutracker: "Сезон: 1 / Серии: 1-10 из 10", "Сезоны: 1-3"; Toloka: "Сезон 2 / Серії 1-8"
        if (RussianSeason().Match(title) is { Success: true } season)
        {
            var episodes = RussianEpisodes().Match(title);
            return (
                int.Parse(season.Groups["from"].Value),
                Optional(season.Groups["to"]),
                episodes.Success ? int.Parse(episodes.Groups["from"].Value) : null,
                episodes.Success ? Optional(episodes.Groups["to"]) : null);
        }

        // Scene: "S01E03", "S01E01-E05", "S01-S03", "S02"
        if (SceneEpisodes().Match(title) is { Success: true } scene)
        {
            return (
                int.Parse(scene.Groups["s"].Value),
                Optional(scene.Groups["s2"]),
                Optional(scene.Groups["e"]),
                Optional(scene.Groups["e2"]));
        }

        if (EnglishSeason().Match(title) is { Success: true } english)
            return (int.Parse(english.Groups["from"].Value), Optional(english.Groups["to"]), null, null);

        return (null, null, null, null);
    }

    private static int? Optional(Group group) => group.Success ? int.Parse(group.Value) : null;

    private static VoiceType ToVoiceType(string token) => token.ToUpperInvariant() switch
    {
        "MVO" => VoiceType.Mvo,
        "DVO" => VoiceType.Dvo,
        "AVO" => VoiceType.Avo,
        "VO" => VoiceType.Vo,
        _ => VoiceType.Dub,
    };

    private static string? ToLanguage(string token) => LanguageCodes.GetValueOrDefault(token.Trim().TrimEnd('.'));

    [GeneratedRegex(@"\b(?<r>2160|1080|720|576|480)[pi]\b", RegexOptions.IgnoreCase)]
    private static partial Regex Resolution();

    [GeneratedRegex(@"\b(?:4K|UHD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UltraHd();

    [GeneratedRegex(@"(?:BD)?Remux", RegexOptions.IgnoreCase)]
    private static partial Regex Remux();

    [GeneratedRegex(@"\b(?:BDRip|BRRip|Blu-?Ray)", RegexOptions.IgnoreCase)]
    private static partial Regex BdRip();

    [GeneratedRegex(@"\bWEB-?(?:DL)?-?Rip\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebRip();

    [GeneratedRegex(@"\bWEB-?DL\b|\bWEB\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebDl();

    [GeneratedRegex(@"\bHDTV(?:Rip)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex HdTv();

    [GeneratedRegex(@"\bHDRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex HdRip();

    [GeneratedRegex(@"\bDVDRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex DvdRip();

    [GeneratedRegex(@"\b(?:CAM(?:Rip)?|HDCAM|TS|HDTS|TeleSync|TC|TeleCine)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cam();

    [GeneratedRegex(@"\b(?:x\.?265|HEVC|H\.?265)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hevc();

    [GeneratedRegex(@"\b(?:x\.?264|AVC|H\.?264)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Avc();

    [GeneratedRegex(@"\bAV1\b", RegexOptions.IgnoreCase)]
    private static partial Regex Av1();

    [GeneratedRegex(@"\b10[- ]?bit\b", RegexOptions.IgnoreCase)]
    private static partial Regex TenBit();

    [GeneratedRegex(@"\b(?:MKV|MP4|AVI)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Container();

    [GeneratedRegex(@"\b(?<type>Dub|Дубляж|MVO|DVO|AVO|VO)\b(?:\s*\((?<details>[^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex VoiceToken();

    [GeneratedRegex(@"\b(?:Dub|MVO|DVO|AVO|VO)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VoiceTypeName();

    [GeneratedRegex(@"(?<![/\w])(?:Ukr|Укр)\b(?!\s*/)(?:\s*\((?<details>[^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex UkrainianToken();

    [GeneratedRegex(@"\bOriginal\b(?:\s+(?<lang>Eng|Rus|Ukr)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex OriginalToken();

    [GeneratedRegex(@"\|\s*(?<codes>(?:[DPAL][12]?)(?:\s*,\s*[DPAL][12]?)*)\s*(?=\||$)")]
    private static partial Regex KinozalCodes();

    [GeneratedRegex(@"\b(?:Ukr|Rus|Eng)(?:/(?:Ukr|Rus|Eng))+\b", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageList();

    [GeneratedRegex(@"\bSub(?:s|titles)?\b\s*[:(]?\s*(?<langs>(?:(?:Rus|Eng|Ukr)\b[\s,/]*)+)", RegexOptions.IgnoreCase)]
    private static partial Regex Subtitles();

    [GeneratedRegex(@"\s+Studio$", RegexOptions.IgnoreCase)]
    private static partial Regex StudioSuffix();

    [GeneratedRegex(@"Сезон(?:ы|и)?\s*:?\s*(?<from>\d{1,2})(?:\s*-\s*(?<to>\d{1,2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex RussianSeason();

    // Russian "Серии", "Серия"; Ukrainian "Серії".
    [GeneratedRegex(@"Сер[иі](?:и|ї|я)\s*:?\s*(?<from>\d{1,3})(?:\s*-\s*(?<to>\d{1,3}))?", RegexOptions.IgnoreCase)]
    private static partial Regex RussianEpisodes();

    [GeneratedRegex(@"\bS(?<s>\d{1,2})(?:-S?(?<s2>\d{1,2}))?(?:E(?<e>\d{1,3})(?:-E?(?<e2>\d{1,3}))?)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SceneEpisodes();

    [GeneratedRegex(@"\bSeasons?\s*(?<from>\d{1,2})(?:\s*-\s*(?<to>\d{1,2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishSeason();
}

public sealed record ParsedRelease(
    int? Resolution,
    ReleaseSource Source,
    string? Codec,
    bool Is10Bit,
    string? Container,
    IReadOnlyList<ReleaseAudio> Audio,
    IReadOnlyList<string> Subtitles,
    int? SeasonFrom,
    int? SeasonTo,
    int? EpisodeFrom,
    int? EpisodeTo);
