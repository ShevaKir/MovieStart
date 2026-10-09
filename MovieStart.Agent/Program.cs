using System.Reflection;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Demo;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Agent.Profile;
using MovieStart.Agent.Search;
using MovieStart.Shared;
using MovieStart.Shared.Health;

var builder = WebApplication.CreateBuilder(args);

// Personal overrides for local development; not committed.
// Environment variables and the command line are added again so they still win, e.g. systemd settings on the Pi.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.Configure<PlayerOptions>(builder.Configuration.GetSection(PlayerOptions.SectionName));
builder.Services.Configure<QbitOptions>(builder.Configuration.GetSection(QbitOptions.SectionName));
builder.Services.Configure<ProwlarrOptions>(builder.Configuration.GetSection(ProwlarrOptions.SectionName));
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.SectionName));
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));

var isDemo = builder.Configuration.GetValue<bool>($"{DemoOptions.SectionName}:{nameof(DemoOptions.Enabled)}");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStorageProbe, DriveStorageProbe>();
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddSingleton<IVoiceProfileStore, FileVoiceProfileStore>();

if (isDemo)
{
    builder.Services.AddDemoMode();
}
else
{
    builder.Services.AddSingleton<MpvPlayer>();
    builder.Services.AddSingleton<IPlayer>(services => services.GetRequiredService<MpvPlayer>());
    builder.Services.AddHostedService(services => services.GetRequiredService<MpvPlayer>());

    // One long-lived client: qBittorrent keeps the login in a session cookie.
    builder.Services.AddSingleton<IQbitClient>(services => new QbitClient(
        new HttpClient(new SocketsHttpHandler
        {
            CookieContainer = new(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        },
        services.GetRequiredService<IOptions<QbitOptions>>()));

    builder.Services.AddHttpClient<IProwlarrClient, ProwlarrClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
}

builder.Services.AddSingleton<ILibraryStore, FileLibraryStore>();
builder.Services.AddSingleton<LibraryService>();
builder.Services.AddHostedService<LibraryWatcher>();
builder.Services.AddHostedService<WatchTracker>();

builder.Services.AddHttpClient<ITmdbClient, TmdbClient>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<SearchService>();

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

app.MapGet(ApiRoutes.Health, () => new HealthResponse(version, DateTimeOffset.UtcNow, isDemo));
app.MapPlayerEndpoints();
app.MapLibraryEndpoints();
app.MapSearchEndpoints();
app.MapProfileEndpoints();
if (isDemo)
    app.MapDemoEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
