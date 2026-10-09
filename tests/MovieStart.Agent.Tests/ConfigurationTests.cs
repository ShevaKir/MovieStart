using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Media;

namespace MovieStart.Agent.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void EnvironmentVariablesWinOverLocalSettings()
    {
        // AgentFactory sets Media:Root the way a host setting or environment variable would.
        var options = _factory.Services.GetRequiredService<IOptions<MediaOptions>>().Value;

        Assert.Equal(_factory.MediaRoot, options.Root);
    }

    [Fact]
    public void LocalSettingsFileIsLoadedBeforeEnvironmentVariables()
    {
        var configuration = (IConfigurationRoot)_factory.Services.GetRequiredService<IConfiguration>();
        var sources = configuration.Providers.Select(provider => provider.ToString() ?? string.Empty).ToList();

        var local = sources.FindLastIndex(source => source.Contains("appsettings.Local.json"));
        var environment = sources.FindLastIndex(source => source.Contains("EnvironmentVariablesConfigurationProvider"));

        Assert.True(local >= 0, "appsettings.Local.json is registered");
        Assert.True(environment > local, "environment variables come after appsettings.Local.json");
    }
}
