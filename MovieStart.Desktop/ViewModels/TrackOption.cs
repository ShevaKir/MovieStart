using MovieStart.Shared.Player;

namespace MovieStart.Desktop.ViewModels;

/// <param name="Id">Player track id; null means "off".</param>
public sealed record TrackOption(int? Id, string Label)
{
    public static TrackOption Off { get; } = new(null, "Off");

    public static TrackOption From(MediaTrack track)
    {
        var parts = new[] { track.Language?.ToUpperInvariant(), track.Title }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(" · ", parts);
        return new TrackOption(track.Id, label.Length > 0 ? label : $"Track {track.Id}");
    }

    public override string ToString() => Label;
}
