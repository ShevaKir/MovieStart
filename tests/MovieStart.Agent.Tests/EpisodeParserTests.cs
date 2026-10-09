using MovieStart.Agent.Library;

namespace MovieStart.Agent.Tests;

public class EpisodeParserTests
{
    [Theory]
    [InlineData("Shogun.S01E02.1080p.WEB-DL.mkv", 1, 2)]
    [InlineData("show.s1e2.mkv", 1, 2)]
    [InlineData("Show S01 E10 Title.mkv", 1, 10)]
    [InlineData("Show.S02.E03.mkv", 2, 3)]
    [InlineData("Show 1x05.mkv", 1, 5)]
    [InlineData("Show/Season 2/03. Title.mkv", 2, 3)]
    [InlineData("Show/S02/E04.mkv", 2, 4)]
    [InlineData("Сериал/Сезон 3/Серия 7.mkv", 3, 7)]
    [InlineData("Сериал/2 сезон/05.mkv", 2, 5)]
    [InlineData("Dune.2021.1080p.mkv", null, null)]
    public void ReadsSeasonAndEpisode(string path, int? season, int? episode)
    {
        Assert.Equal((season, episode), EpisodeParser.Parse(path));
    }
}
