using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MovieStart.Client;
using MovieStart.Web;
using MovieStart.Web.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The agent serves this app, so its API lives at the same address.
var agent = new AgentAddress(builder.HostEnvironment.BaseAddress);
builder.Services.AddSingleton(agent);

// AgentClient sets a timeout per call.
builder.Services.AddSingleton<IAgentClient>(new AgentClient(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }));
builder.Services.AddSingleton<Notifications>();
builder.Services.AddSingleton<LibraryStore>();
builder.Services.AddSingleton<PlayerStore>();

await builder.Build().RunAsync();
