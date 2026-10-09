using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Health;

namespace MovieStart.Desktop.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void StartsInCheckingState()
    {
        var viewModel = new MainWindowViewModel(new FakeAgentClient(null), "http://pi.local:5080");

        Assert.Equal(ConnectionStatus.Checking, viewModel.Status);
        Assert.Equal("Checking…", viewModel.StatusText);
    }

    [Fact]
    public async Task BecomesOnlineWhenAgentResponds()
    {
        var viewModel = new MainWindowViewModel(
            new FakeAgentClient(new HealthResponse("1.0.0", DateTimeOffset.UtcNow)), "http://pi.local:5080");

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsOnline);
        Assert.False(viewModel.IsOffline);
        Assert.Equal("Online · agent 1.0.0", viewModel.StatusText);
    }

    [Fact]
    public async Task BecomesOfflineWhenAgentIsUnreachable()
    {
        var viewModel = new MainWindowViewModel(new FakeAgentClient(null), "http://pi.local:5080");

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsOffline);
        Assert.Equal("Offline", viewModel.StatusText);
    }

    [Fact]
    public async Task ChecksTheCurrentAgentUrl()
    {
        var agentClient = new FakeAgentClient(null);
        var viewModel = new MainWindowViewModel(agentClient, "http://pi.local:5080")
        {
            AgentUrl = "http://localhost:5080",
        };

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:5080", agentClient.LastAgentUrl);
    }

    private sealed class FakeAgentClient(HealthResponse? health) : IAgentClient
    {
        public string? LastAgentUrl { get; private set; }

        public Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default)
        {
            LastAgentUrl = agentUrl;
            return Task.FromResult(health);
        }
    }
}
