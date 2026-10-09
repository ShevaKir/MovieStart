using MovieStart.Shared.Player;

namespace MovieStart.Agent.Player;

public interface IPlayer
{
    PlayerState State { get; }

    /// <exception cref="PlayerUnavailableException">The player process is not reachable.</exception>
    /// <exception cref="PlayerCommandException">The player rejected the command.</exception>
    Task PlayAsync(string path, CancellationToken cancellationToken);

    /// <exception cref="PlayerUnavailableException">The player process is not reachable.</exception>
    /// <exception cref="PlayerCommandException">The player rejected the command.</exception>
    /// <exception cref="ArgumentException">The command is missing a required value.</exception>
    Task SendAsync(PlayerCommand command, CancellationToken cancellationToken);
}

public sealed class PlayerUnavailableException() : Exception("The player is not running.");

public sealed class PlayerCommandException(string command, string? error)
    : Exception($"The player rejected '{command}': {error}.");
