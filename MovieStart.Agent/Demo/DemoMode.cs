using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Agent.Player;
using MovieStart.Agent.Search;
using MovieStart.Shared;

namespace MovieStart.Agent.Demo;

public static class DemoMode
{
    /// <summary>Replaces Prowlarr, qBittorrent and mpv with simulations; TMDB and ffprobe stay real.</summary>
    public static IServiceCollection AddDemoMode(this IServiceCollection services)
    {
        services.AddSingleton<DemoClipFactory>();
        services.AddSingleton<DemoQbitClient>();
        services.AddSingleton<IQbitClient>(provider => provider.GetRequiredService<DemoQbitClient>());
        services.AddSingleton<IProwlarrClient, DemoProwlarrClient>();
        services.AddSingleton<DemoPlayer>();
        services.AddSingleton<IPlayer>(provider => provider.GetRequiredService<DemoPlayer>());
        services.AddHostedService(provider => provider.GetRequiredService<DemoPlayer>());
        return services;
    }

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.DemoCompleteDownloads, (DemoQbitClient qbit) =>
        {
            qbit.CompleteAll();
            return Results.NoContent();
        });

        app.MapPost(ApiRoutes.DemoReset, async (LibraryService library, CancellationToken cancellationToken) =>
        {
            foreach (var item in library.List())
                await library.DeleteAsync(item.Id, cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
