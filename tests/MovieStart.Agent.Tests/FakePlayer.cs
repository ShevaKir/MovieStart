using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Tests;

public sealed class FakePlayer : IPlayer
{
    public PlayerState State { get; set; } = PlayerState.Disconnected;

    public string? PlayedPath { get; private set; }

    public double? PlayedStart { get; private set; }

    public PlayerCommand? LastCommand { get; private set; }

    public Exception? Failure { get; set; }

    public Task PlayAsync(string path, double startSeconds, CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        PlayedPath = path;
        PlayedStart = startSeconds;
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
