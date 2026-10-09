using System.Text.Json.Serialization;

namespace MovieStart.Shared.Library;

[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
    Movie,
    Series,
}
