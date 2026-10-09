using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using MovieStart.Desktop.Services;
using MovieStart.Desktop.ViewModels;
using MovieStart.Desktop.Views;

namespace MovieStart.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = LoadOptions();
            var agentClient = new AgentClient(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(agentClient, options.AgentUrl),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static DesktopOptions LoadOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            // Personal overrides for local development; not committed.
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables(prefix: "MOVIESTART_")
            .Build();

        return configuration.Get<DesktopOptions>() ?? new DesktopOptions();
    }
}
