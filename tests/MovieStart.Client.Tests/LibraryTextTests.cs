using MovieStart.Shared.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Client.Tests;

public class LibraryTextTests
{
    private static readonly Guid DownloadId = Guid.NewGuid();

    private static MediaFile Episode(int id, int episode, double position = 0, bool watched = false) =>
        new(id, DownloadId, $"S01E0{episode}.mkv", 1, 1, episode, position, 3000, watched);

    private static LibraryItem Series(int? lastPlayed, params MediaFile[] files) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MediaKind.Series,
        Title = "The Bear",
        Year = 2022,
        Files = files,
        LastPlayedFileId = lastPlayed,
    };

    [Fact]
    public void OffersToWatchWhenNothingWasPlayed()
    {
        var item = Series(null, Episode(1, 1), Episode(2, 2));

        Assert.Equal("Watch S01E01", item.PlayLabel());
        Assert.False(item.CanStartOver());
    }

    [Fact]
    public void ContinuesTheStoppedEpisodeFromItsPosition()
    {
        var item = Series(2, Episode(1, 1, watched: true), Episode(2, 2, position: 750));

        Assert.Equal("Continue S01E02 from 12:30", item.PlayLabel());
        Assert.True(item.CanStartOver());
    }

    [Fact]
    public void OffersTheNextEpisodeOnceTheLastOneIsFinished()
    {
        var item = Series(1, Episode(1, 1, position: 2990, watched: true), Episode(2, 2));

        Assert.Equal("Next S01E02", item.PlayLabel());
        Assert.False(item.CanStartOver());
    }

    [Fact]
    public void SummarizesYearKindAndSize()
    {
        var item = Series(null, Episode(1, 1) with { SizeBytes = 2L << 30 });

        Assert.Equal("2022 · Series · 2.0 GB", item.Summary());
        Assert.Equal("TB", item.Initials());
    }

    [Fact]
    public void DescribesARunningDownload()
    {
        var item = Series(null) with
        {
            Downloads =
            [
                new LibraryDownload
                {
                    Id = DownloadId,
                    Status = DownloadStatus.Downloading,
                    Progress = 0.42,
                    DownloadSpeed = 3L << 20,
                    EtaSeconds = 300,
                },
            ],
        };

        Assert.Equal("Downloading 42% · 3.0 MB/s · 5 min left", item.DownloadStatusText());
    }

    [Fact]
    public void ReadyDownloadsNeedNoStatus()
    {
        var item = Series(null) with { Downloads = [new LibraryDownload { Id = DownloadId, Status = DownloadStatus.Ready }] };

        Assert.Null(item.DownloadStatusText());
    }

    [Theory]
    [InlineData("ukr", "Dub", "UKR · Dub")]
    [InlineData(null, null, "Track 3")]
    public void LabelsTracks(string? language, string? title, string expected) =>
        Assert.Equal(expected, new MediaTrack(3, language, title).Label());
}
