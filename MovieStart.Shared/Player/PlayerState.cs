namespace MovieStart.Shared.Player;

public sealed record PlayerState
{
    public static PlayerState Disconnected { get; } = new() { IsConnected = false };

    /// <summary>Whether the agent is connected to the player process.</summary>
    public bool IsConnected { get; init; }

    /// <summary>True when nothing is loaded.</summary>
    public bool IsIdle { get; init; } = true;

    public string? FilePath { get; init; }

    public string? Title { get; init; }

    public bool IsPaused { get; init; }

    /// <summary>Playback position in seconds.</summary>
    public double Position { get; init; }

    /// <summary>Media duration in seconds; 0 when unknown.</summary>
    public double Duration { get; init; }

    /// <summary>Volume, 0–100.</summary>
    public int Volume { get; init; } = 100;

    public IReadOnlyList<MediaTrack> AudioTracks { get; init; } = [];

    public IReadOnlyList<MediaTrack> SubtitleTracks { get; init; } = [];

    public int? AudioTrackId { get; init; }

    /// <summary>Selected subtitle track; null when subtitles are off.</summary>
    public int? SubtitleTrackId { get; init; }
}
