using System.Text.Json;
using MovieStart.Shared.Health;

namespace MovieStart.Shared.Tests;

public class HealthResponseTests
{
    // The agent serializes with ASP.NET Core web defaults (camelCase); the desktop must read it back.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void RoundTripsThroughWebJson()
    {
        var original = new HealthResponse("1.2.3", new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(original, WebOptions);
        var restored = JsonSerializer.Deserialize<HealthResponse>(json, WebOptions);

        Assert.Equal(original, restored);
        Assert.Contains("\"version\":\"1.2.3\"", json);
    }
}
