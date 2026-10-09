using System.Reflection;
using MovieStart.Agent.Player;
using MovieStart.Shared;
using MovieStart.Shared.Health;

var builder = WebApplication.CreateBuilder(args);

// Personal overrides for local development; not committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<PlayerOptions>(builder.Configuration.GetSection(PlayerOptions.SectionName));
builder.Services.AddSingleton<MpvPlayer>();
builder.Services.AddSingleton<IPlayer>(services => services.GetRequiredService<MpvPlayer>());
builder.Services.AddHostedService(services => services.GetRequiredService<MpvPlayer>());

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

app.MapGet(ApiRoutes.Health, () => new HealthResponse(version, DateTimeOffset.UtcNow));
app.MapPlayerEndpoints();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
