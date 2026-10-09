using MovieStart.Agent.Player;

namespace MovieStart.Agent.Tests;

public class MediaPathTests
{
    [Theory]
    [InlineData("/mnt/movies/Dune/Dune.mkv", true)]
    [InlineData("/mnt/movies/../etc/passwd", false)]
    [InlineData("/mnt/movies-other/Dune.mkv", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("Dune.mkv", false)]
    [InlineData("", false)]
    public void AllowsOnlyFilesInsideMediaRoot(string path, bool allowed)
    {
        Assert.Equal(allowed, MediaPath.TryResolve("/mnt/movies", path, out _));
    }
}
