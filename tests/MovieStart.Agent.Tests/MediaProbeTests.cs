using MovieStart.Agent.Media;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public class MediaProbeTests
{
    [Fact]
    public void ParsesFfprobeOutput()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "hevc" },
                { "index": 1, "codec_type": "audio", "tags": { "language": "rus", "title": "Дубляж, Blu-ray CEE" } },
                { "index": 2, "codec_type": "audio", "tags": { "language": "ukr", "title": "Дубляж" } },
                { "index": 3, "codec_type": "audio", "tags": { "language": "und" } },
                { "index": 4, "codec_type": "subtitle", "tags": { "language": "eng", "title": "Forced" } }
              ],
              "format": { "duration": "9301.248000" }
            }
            """;

        var result = FfprobeMediaProbe.Parse(json)!;

        Assert.Equal(9301.248, result.Duration);
        Assert.Equal(
            [new MediaTrack(1, "rus", "Дубляж, Blu-ray CEE"), new MediaTrack(2, "ukr", "Дубляж"), new MediaTrack(3, null, null)],
            result.Audio);
        Assert.Equal([new MediaTrack(1, "eng", "Forced")], result.Subtitles);
    }

    [Fact]
    public void BrokenOutputIsNull()
    {
        Assert.Null(FfprobeMediaProbe.Parse("not json"));
    }
}
