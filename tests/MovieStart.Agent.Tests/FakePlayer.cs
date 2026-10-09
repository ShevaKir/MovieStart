using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class FakePlayer : IPlayer
{
    public PlayerState State { get; set; } = PlayerState.Disconnected;

    public string? PlayedPath { get; private set; }

    public PlaybackOptions? PlayedOptions { get; private set; }

    public double? PlayedStart => PlayedOptions?.StartSeconds;

    public PlayerCommand? LastCommand { get; private set; }

    public Exception? Failure { get; set; }

    public Task PlayAsync(string path, PlaybackOptions options, CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        PlayedPath = path;
        PlayedOptions = options;
        return Task.CompletedTask;
    }

    public Task SendAsync(PlayerCommand command, CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        LastCommand = command;
        return Task.CompletedTask;
    }
}
