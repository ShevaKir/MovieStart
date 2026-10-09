using System.Reflection;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Downloads;
using MovieStart.Agent.Library;
using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Shared;
using MovieStart.Shared.Health;

var builder = WebApplication.CreateBuilder(args);

// Personal overrides for local development; not committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.SectionName));
builder.Services.Configure<PlayerOptions>(builder.Configuration.GetSection(PlayerOptions.SectionName));
builder.Services.Configure<QbitOptions>(builder.Configuration.GetSection(QbitOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStorageProbe, DriveStorageProbe>();

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

builder.Services.AddSingleton<ILibraryStore, FileLibraryStore>();
builder.Services.AddSingleton<LibraryService>();
builder.Services.AddHostedService<LibraryWatcher>();
builder.Services.AddHostedService<WatchTracker>();

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

app.MapGet(ApiRoutes.Health, () => new HealthResponse(version, DateTimeOffset.UtcNow));
app.MapPlayerEndpoints();
app.MapLibraryEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
