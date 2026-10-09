using System.Text.Json.Serialization;

namespace MovieStart.Shared.Player;

[JsonConverter(typeof(JsonStringEnumConverter<PlayerCommandType>))]
public enum PlayerCommandType
{
    Pause,
    Resume,

    /// <summary>Seek by <see cref="PlayerCommand.Value"/> seconds; negative goes back.</summary>
    SeekRelative,

    /// <summary>Seek to <see cref="PlayerCommand.Value"/> seconds from the start.</summary>
    SeekAbsolute,

    /// <summary>Set volume to <see cref="PlayerCommand.Value"/> (0–100).</summary>
    SetVolume,

    /// <summary>Select audio track with id <see cref="PlayerCommand.Value"/>.</summary>
    SetAudioTrack,

    /// <summary>Select subtitle track with id <see cref="PlayerCommand.Value"/>; null turns subtitles off.</summary>
    SetSubtitleTrack,

    Stop,
}

public sealed record PlayerCommand(PlayerCommandType Type, double? Value = null)
{
    public static PlayerCommand Pause() => new(PlayerCommandType.Pause);

    public static PlayerCommand Resume() => new(PlayerCommandType.Resume);

    public static PlayerCommand SeekRelative(double seconds) => new(PlayerCommandType.SeekRelative, seconds);

    public static PlayerCommand SeekAbsolute(double seconds) => new(PlayerCommandType.SeekAbsolute, seconds);

    public static PlayerCommand SetVolume(int volume) => new(PlayerCommandType.SetVolume, volume);

    public static PlayerCommand SetAudioTrack(int trackId) => new(PlayerCommandType.SetAudioTrack, trackId);

    public static PlayerCommand SetSubtitleTrack(int? trackId) => new(PlayerCommandType.SetSubtitleTrack, trackId);

    public static PlayerCommand Stop() => new(PlayerCommandType.Stop);
}
