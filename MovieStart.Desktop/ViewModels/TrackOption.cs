using MovieStart.Client;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.ViewModels;

/// <param name="Id">Player track id; null means "off".</param>
public sealed record TrackOption(int? Id, string Label)
{
    public static TrackOption Off { get; } = new(null, "Off");

    public static TrackOption From(MediaTrack track) => new(track.Id, track.Label());

    public override string ToString() => Label;
}
