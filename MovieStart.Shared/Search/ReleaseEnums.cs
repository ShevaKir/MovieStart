using System.Text.Json.Serialization;

namespace MovieStart.Shared.Search;

/// <summary>Where the video comes from, best first.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReleaseSource>))]
public enum ReleaseSource
{
    Unknown,
    Remux,
    BdRip,
    WebDl,
    WebRip,
    HdTv,
    HdRip,
    DvdRip,

    /// <summary>Recorded in a cinema; filtered out.</summary>
    Cam,
}

[JsonConverter(typeof(JsonStringEnumConverter<VoiceType>))]
public enum VoiceType
{
    /// <summary>Full dubbing.</summary>
    Dub,

    /// <summary>Multi-voice voice-over.</summary>
    Mvo,

    /// <summary>Two-voice voice-over.</summary>
    Dvo,

    /// <summary>Single-voice voice-over by a known author.</summary>
    Avo,

    /// <summary>Single-voice voice-over.</summary>
    Vo,
    Original,
}
