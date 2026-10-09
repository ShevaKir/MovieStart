using System.Reflection;
using MovieStart.Shared;
using MovieStart.Shared.Health;

var builder = WebApplication.CreateBuilder(args);

// Personal overrides for local development; not committed.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

app.MapGet(ApiRoutes.Health, () => new HealthResponse(version, DateTimeOffset.UtcNow));

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
