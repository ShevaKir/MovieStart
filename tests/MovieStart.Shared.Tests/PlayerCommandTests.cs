using System.Text.Json;
using MovieStart.Shared.Player;

namespace MovieStart.Shared.Tests;

public class PlayerCommandTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SerializesTypeAsString()
    {
        var json = JsonSerializer.Serialize(PlayerCommand.SetSubtitleTrack(null), WebOptions);

        Assert.Equal("""{"type":"SetSubtitleTrack","value":null}""", json);
    }

    [Fact]
    public void RoundTripsThroughWebJson()
    {
        var command = PlayerCommand.SeekAbsolute(600);

        var restored = JsonSerializer.Deserialize<PlayerCommand>(JsonSerializer.Serialize(command, WebOptions), WebOptions);

        Assert.Equal(command, restored);
    }
}
