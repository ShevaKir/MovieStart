using MovieStart.Agent.Player;

namespace MovieStart.Agent.Tests;

public sealed class AudioOutputTests
{
    [Fact]
    public void ReadsTheSinkNameFromWpctl()
    {
        const string output = """
            id 33, type PipeWire:Interface:Node
              * media.class = "Audio/Sink"
              * node.description = "Dummy Output"
              * node.name = "auto_null"
            """;

        Assert.Equal(PipeWireAudioOutput.DummySink, PipeWireAudioOutput.ParseNodeName(output));
    }

    [Fact]
    public void NoSinkNameWhenWpctlPrintsNone()
    {
        Assert.Null(PipeWireAudioOutput.ParseNodeName("Object '@DEFAULT_AUDIO_SINK@' not found"));
    }
}
