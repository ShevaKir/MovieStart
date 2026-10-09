using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Tests;

public class FileSelectionTests
{
    [Theory]
    [InlineData("Dune/Sample/dune.mkv", true)]
    [InlineData("Dune/dune-sample.mkv", true)]
    [InlineData("Dune/Extras/Making of.mkv", true)]
    [InlineData("Dune/Featurettes/Desert.mkv", true)]
    [InlineData("Dune/Behind The Scenes/Part 1.mkv", true)]
    [InlineData("Dune/Trailer.mkv", true)]
    [InlineData("Dune/Dune.2021.1080p.mkv", false)]
    [InlineData("Shogun/Shogun.S01E05.Sampled.Blades.mkv", false)]
    [InlineData("Sample Show/Sample.Show.S01E01.mkv", false)]
    public void RecognizesJunk(string name, bool junk)
    {
        Assert.Equal(junk, FileSelection.IsJunk(name));
    }

    [Fact]
    public void MovieKeepsOnlyTheMainVideoAndItsSubtitles()
    {
        QbitFile[] files =
        [
            new() { Index = 0, Name = "Dune/Dune.mkv", Size = 6_000 },
            new() { Index = 1, Name = "Dune/Dune.Interview.mkv", Size = 500 },
            new() { Index = 2, Name = "Dune/Subs/Rus.srt", Size = 50 },
            new() { Index = 3, Name = "Dune/Sample/sample.mkv", Size = 100 },
        ];

        Assert.Equal([1, 3], FileSelection.AutoSkip(MediaKind.Movie, files).Order());
    }

    [Fact]
    public void SeriesKeepsEveryEpisode()
    {
        QbitFile[] files =
        [
            new() { Index = 0, Name = "S01/E01.mkv", Size = 2_000 },
            new() { Index = 1, Name = "S01/E02.mkv", Size = 1_000 },
        ];

        Assert.Empty(FileSelection.AutoSkip(MediaKind.Series, files));
    }
}
