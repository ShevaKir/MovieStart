using MovieStart.Desktop.Services;
using MovieStart.Shared.Health;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.Tests;

public sealed class FakeAgentClient : IAgentClient
{
    public HealthResponse? Health { get; set; }

    public PlayerState? PlayerState { get; set; }

    public AgentResult Result { get; set; } = AgentResult.Success;

    public string? LastAgentUrl { get; private set; }

    public string? PlayedPath { get; private set; }

    public List<PlayerCommand> Commands { get; } = [];

    public Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        return Task.FromResult(Health);
    }

    public Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        return Task.FromResult(PlayerState);
    }

    public Task<AgentResult> PlayAsync(string agentUrl, string path, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        PlayedPath = path;
        return Task.FromResult(Result);
    }

    public Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        Commands.Add(command);
        return Task.FromResult(Result);
    }
}
