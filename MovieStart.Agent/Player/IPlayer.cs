using MovieStart.Shared.Player;

namespace MovieStart.Agent.Player;

public interface IPlayer
{
    PlayerState State { get; }

    /// <exception cref="PlayerUnavailableException">The player process is not reachable.</exception>
    /// <exception cref="PlayerCommandException">The player rejected the command.</exception>
    Task PlayAsync(string path, PlaybackOptions options, CancellationToken cancellationToken);

    /// <exception cref="PlayerUnavailableException">The player process is not reachable.</exception>
    /// <exception cref="PlayerCommandException">The player rejected the command.</exception>
    /// <exception cref="ArgumentException">The command is missing a required value.</exception>
    Task SendAsync(PlayerCommand command, CancellationToken cancellationToken);
}

/// <param name="StartSeconds">Position to start from; 0 plays from the beginning.</param>
/// <param name="AudioTrackId">Audio track to select; null keeps the player's default.</param>
/// <param name="Subtitles">Subtitle choice; null keeps the player's default.</param>
public sealed record PlaybackOptions(double StartSeconds = 0, int? AudioTrackId = null, SubtitleChoice? Subtitles = null);

/// <param name="TrackId">Subtitle track to show; null turns subtitles off.</param>
public sealed record SubtitleChoice(int? TrackId);

public sealed class PlayerUnavailableException() : Exception("The player is not running.");

public sealed class PlayerCommandException(string command, string? error)
    : Exception($"The player rejected '{command}': {error}.");
