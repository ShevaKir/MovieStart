using MovieStart.Agent.Search;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Tests;

public class ReleaseParserTests
{
    [Fact]
    public void RutrackerMovie()
    {
        var release = ReleaseParser.Parse(
            "Дюна / Dune (Дени Вильнёв / Denis Villeneuve) [2021, США, Канада, фантастика, BDRip 1080p] " +
            "Dub + MVO (HDRezka Studio) + AVO (Гаврилов) + Ukr (Dub) + Original Eng + Sub Rus, Eng");

        Assert.Equal(1080, release.Resolution);
        Assert.Equal(ReleaseSource.BdRip, release.Source);
        Assert.Equal(
            [
                new ReleaseAudio("rus", VoiceType.Dub),
                new ReleaseAudio("rus", VoiceType.Mvo, "HDRezka"),
                new ReleaseAudio("rus", VoiceType.Avo, "Гаврилов"),
                new ReleaseAudio("ukr", VoiceType.Dub),
                new ReleaseAudio("eng", VoiceType.Original),
            ],
            release.Audio);
        Assert.Equal(["rus", "eng"], release.Subtitles);
        Assert.Null(release.SeasonFrom);
    }

    [Fact]
    public void RutrackerSeason()
    {
        var release = ReleaseParser.Parse(
            "Сёгун / Shōgun / Сезон: 1 / Серии: 1-10 из 10 (Джонатан ван Туллекен) [2024, США, драма, WEB-DL 1080p] " +
            "MVO (LostFilm) + Ukr (Le Doyen) + Original + Sub (Rus, Eng)");

        Assert.Equal(ReleaseSource.WebDl, release.Source);
        Assert.Equal((1, (int?)null, 1, 10), (release.SeasonFrom!.Value, release.SeasonTo, release.EpisodeFrom!.Value, release.EpisodeTo));
        Assert.Equal(
            [
                new ReleaseAudio("rus", VoiceType.Mvo, "LostFilm"),
                new ReleaseAudio("ukr", VoiceType.Dub, "Le Doyen"),
                new ReleaseAudio("eng", VoiceType.Original),
            ],
            release.Audio);
        Assert.Equal(["rus", "eng"], release.Subtitles);
    }

    [Fact]
    public void RutrackerSeveralSeasons()
    {
        var release = ReleaseParser.Parse("Ведьмак / The Witcher / Сезоны: 1-3 / Серии: 1-24 из 24 [2019-2023, WEB-DLRip 1080p] MVO (Ukr)");

        Assert.Equal(ReleaseSource.WebRip, release.Source);
        Assert.Equal((1, 3), (release.SeasonFrom!.Value, release.SeasonTo!.Value));
        Assert.Equal([new ReleaseAudio("ukr", VoiceType.Mvo)], release.Audio);
    }

    [Fact]
    public void KinozalCodes()
    {
        var release = ReleaseParser.Parse("Оппенгеймер / Oppenheimer / 2023 / ДБ, ПМ, СТ / BDRip-HEVC (1080p) | 10-bit | D, P, A | Sub");

        Assert.Equal(ReleaseSource.BdRip, release.Source);
        Assert.Equal("x265", release.Codec);
        Assert.True(release.Is10Bit);
        Assert.Equal(
            [new ReleaseAudio("rus", VoiceType.Dub), new ReleaseAudio("rus", VoiceType.Mvo), new ReleaseAudio("rus", VoiceType.Avo)],
            release.Audio);
    }

    [Fact]
    public void TolokaLanguageList()
    {
        var release = ReleaseParser.Parse("Дюна: Частина друга / Dune: Part Two (2024) 1080p WEB-DL Ukr/Eng | Sub Ukr/Eng", defaultLanguage: "ukr");

        Assert.Equal([new ReleaseAudio("ukr", VoiceType.Dub), new ReleaseAudio("eng", VoiceType.Original)], release.Audio);
        Assert.Equal(["ukr", "eng"], release.Subtitles);
    }

    [Theory]
    [InlineData("Дюна / Dune: Part One (2021) UHD BDRip-HEVC 1080p от RIPS CLUB | HDR | D", true)]
    [InlineData("Dune.2021.1080p.BluRay.HDR10+.x265", true)]
    [InlineData("Dune: Prophecy [S01] (2024) WEB-DL 1080p | Dolby Vision Profile 8", true)]
    [InlineData("Дюна / Dune (2021) HDRip 1080p | D", false)]
    public void DetectsHdr(string title, bool isHdr)
    {
        Assert.Equal(isHdr, ReleaseParser.Parse(title).IsHdr);
    }

    [Fact]
    public void TolokaTrackCount()
    {
        var release = ReleaseParser.Parse("Dune: Part Two (2024) BDRip 1080p H.265 2xUkr/Eng | sub 2xUkr/Eng", defaultLanguage: "ukr");

        Assert.Equal([new ReleaseAudio("ukr", VoiceType.Dub), new ReleaseAudio("eng", VoiceType.Original)], release.Audio);
        Assert.Equal(["ukr", "eng"], release.Subtitles);
    }

    [Fact]
    public void TolokaSeasonInUkrainian()
    {
        var release = ReleaseParser.Parse("Сьоґун (Сезон 1, серії 1-10) / Shogun (Season 1) (2024) WEB-DL 1080p Ukr/Eng", defaultLanguage: "ukr");

        Assert.Equal(1, release.SeasonFrom);
        Assert.Equal((1, 10), (release.EpisodeFrom!.Value, release.EpisodeTo!.Value));
    }

    [Fact]
    public void SceneMovie()
    {
        var release = ReleaseParser.Parse("Dune.2021.1080p.BluRay.x265.10bit.AAC5.1-RARBG");

        Assert.Equal((1080, ReleaseSource.BdRip, "x265", true), (release.Resolution, release.Source, release.Codec, release.Is10Bit));
        Assert.Empty(release.Audio);
    }

    [Theory]
    [InlineData("Shogun.2024.S01E03.1080p.WEB-DL.DDP5.1.H.265-NTb", 1, null, 3, null)]
    [InlineData("Shogun S01 1080p WEB-DL x265 HEVC", 1, null, null, null)]
    [InlineData("Show.S01E01-E05.1080p.WEB", 1, null, 1, 5)]
    [InlineData("Show.S01-S03.1080p.BluRay", 1, 3, null, null)]
    [InlineData("Show Season 2 1080p", 2, null, null, null)]
    public void SceneEpisodes(string title, int seasonFrom, int? seasonTo, int? episodeFrom, int? episodeTo)
    {
        var release = ReleaseParser.Parse(title);

        Assert.Equal((seasonFrom, seasonTo, episodeFrom, episodeTo), (release.SeasonFrom!.Value, release.SeasonTo, release.EpisodeFrom, release.EpisodeTo));
    }

    [Theory]
    [InlineData("Movie (2024) CAMRip", ReleaseSource.Cam)]
    [InlineData("Movie.2024.HDTS.x264", ReleaseSource.Cam)]
    [InlineData("Movie [2024, TS 1080p]", ReleaseSource.Cam)]
    [InlineData("Movie [2021, BDRemux 1080p]", ReleaseSource.Remux)]
    [InlineData("Movie [2021, HDRip]", ReleaseSource.HdRip)]
    [InlineData("Movie [2021, HDTVRip 1080p]", ReleaseSource.HdTv)]
    [InlineData("Movie 2021 1080p", ReleaseSource.Unknown)]
    public void Sources(string title, ReleaseSource expected)
    {
        Assert.Equal(expected, ReleaseParser.Parse(title).Source);
    }

    [Theory]
    [InlineData("Movie 2160p UHD", 2160)]
    [InlineData("Movie 4K HDR", 2160)]
    [InlineData("Movie 720p", 720)]
    [InlineData("Movie 1080i HDTV", 1080)]
    [InlineData("Movie DVDRip", null)]
    public void Resolutions(string title, int? expected)
    {
        Assert.Equal(expected, ReleaseParser.Parse(title).Resolution);
    }

    [Fact]
    public void DefaultLanguageAppliesToUnnamedVoices()
    {
        var release = ReleaseParser.Parse("Фільм (2024) WEB-DL 1080p Dub", defaultLanguage: "ukr");

        Assert.Equal([new ReleaseAudio("ukr", VoiceType.Dub)], release.Audio);
    }

    [Fact]
    public void ContainerIsRead()
    {
        Assert.Equal("MKV", ReleaseParser.Parse("Dune.2021.1080p.BDRip.x265.mkv").Container);
    }
}
