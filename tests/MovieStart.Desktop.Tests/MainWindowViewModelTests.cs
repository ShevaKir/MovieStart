using MovieStart.Desktop.ViewModels;
using MovieStart.Shared.Health;

namespace MovieStart.Desktop.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void StartsInCheckingState()
    {
        var viewModel = new MainWindowViewModel(new FakeAgentClient(), "http://pi.local:5080", new NoPosters());

        Assert.Equal(ConnectionStatus.Checking, viewModel.Status);
        Assert.Equal("Checking…", viewModel.StatusText);
    }

    [Fact]
    public async Task BecomesOnlineWhenAgentResponds()
    {
        var agent = new FakeAgentClient { Health = new HealthResponse("1.0.0", DateTimeOffset.UtcNow) };
        var viewModel = new MainWindowViewModel(agent, "http://pi.local:5080", new NoPosters());

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsOnline);
        Assert.False(viewModel.IsOffline);
        Assert.Equal("Online · agent 1.0.0", viewModel.StatusText);
    }

    [Fact]
    public async Task BecomesOfflineWhenAgentIsUnreachable()
    {
        var viewModel = new MainWindowViewModel(new FakeAgentClient(), "http://pi.local:5080", new NoPosters());

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsOffline);
        Assert.Equal("Offline", viewModel.StatusText);
    }

    [Fact]
    public async Task ChecksTheCurrentAgentUrl()
    {
        var agent = new FakeAgentClient();
        var viewModel = new MainWindowViewModel(agent, "http://pi.local:5080", new NoPosters())
        {
            AgentUrl = "http://localhost:5080",
        };

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:5080", agent.LastAgentUrl);
    }

    [Fact]
    public async Task PlayerUsesTheCurrentAgentUrl()
    {
        var agent = new FakeAgentClient();
        var viewModel = new MainWindowViewModel(agent, "http://pi.local:5080", new NoPosters()) { AgentUrl = "http://localhost:5080" };

        await viewModel.Player.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:5080", agent.LastAgentUrl);
    }

    [Fact]
    public async Task DemoAgentShowsTheDemoMenu()
    {
        var agent = new FakeAgentClient { Health = new HealthResponse("1.0.0", DateTimeOffset.UtcNow, IsDemo: true) };
        var viewModel = new MainWindowViewModel(agent, "http://localhost:5080", new NoPosters());

        await viewModel.CheckConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsDemo);
        Assert.Equal("Demo · agent 1.0.0", viewModel.StatusText);
    }

    [Fact]
    public async Task DemoButtonsCallTheAgent()
    {
        var agent = new FakeAgentClient();
        var viewModel = new MainWindowViewModel(agent, "http://localhost:5080", new NoPosters());

        await viewModel.CompleteDemoDownloadsCommand.ExecuteAsync(null);
        await viewModel.ResetDemoCommand.ExecuteAsync(null);

        Assert.Equal(["demo complete", "demo reset"], agent.Calls);
        Assert.Equal("Demo library cleared.", viewModel.DemoStatus);
    }

    [Fact]
    public async Task OpeningSettingsLoadsTheVoiceProfile()
    {
        var agent = new FakeAgentClient();
        var viewModel = new MainWindowViewModel(agent, "http://localhost:5080", new NoPosters());

        await viewModel.ShowSettingsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSettingsSection);
        Assert.Equal(4, viewModel.VoiceProfile.Preferences.Count);
    }
}
