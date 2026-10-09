using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MovieStart.Agent.Tests;

/// <summary>Runs the agent against a temporary media root, with no mpv or qBittorrent around.</summary>
public sealed class AgentFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? _configureServices;

    public AgentFactory(Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
        MediaRoot = Directory.CreateTempSubdirectory("movies-").FullName;
    }

    public string MediaRoot { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Media:Root", MediaRoot);
        builder.UseSetting("Player:SocketPath", Path.Combine(MediaRoot, "none.sock"));
        builder.UseSetting("QBittorrent:BaseUrl", "http://127.0.0.1:1");
        if (_configureServices is not null)
            builder.ConfigureTestServices(_configureServices);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(MediaRoot))
            Directory.Delete(MediaRoot, recursive: true);
    }
}
