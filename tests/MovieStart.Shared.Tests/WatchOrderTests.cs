using MovieStart.Shared.Library;

namespace MovieStart.Shared.Tests;

public class WatchOrderTests
{
    [Theory]
    [InlineData(null, false, 1)]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 2)]
    [InlineData(3, true, 1)]
    public void ChoosesFileToContinue(int? lastPlayed, bool lastWatched, int expected)
    {
        var files = Enumerable.Range(1, 3)
            .Select(id => new MediaFile(id, Guid.Empty, $"S01E0{id}.mkv", 1, 1, id, Watched: id == lastPlayed && lastWatched))
            .ToList();
        var item = new LibraryItem { Id = Guid.NewGuid(), Title = "Shogun", Files = files, LastPlayedFileId = lastPlayed };

        Assert.Equal(expected, item.ChooseFileToContinue()?.Id);
    }

    [Fact]
    public void SortsBySeasonThenEpisodeThenName()
    {
        MediaFile[] files =
        [
            new(1, Guid.Empty, "b.mkv", 1),
            new(2, Guid.Empty, "S02E01.mkv", 1, 2, 1),
            new(3, Guid.Empty, "S01E10.mkv", 1, 1, 10),
            new(4, Guid.Empty, "a.mkv", 1),
            new(5, Guid.Empty, "S01E02.mkv", 1, 1, 2),
        ];

        Assert.Equal([5, 3, 2, 4, 1], WatchOrder.Sort(files).Select(f => f.Id));
    }
}
