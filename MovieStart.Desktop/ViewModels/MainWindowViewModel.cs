using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;

namespace MovieStart.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAgentClient _agentClient;

    [ObservableProperty]
    private string _agentUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(IsOffline), nameof(StatusText))]
    private ConnectionStatus _status = ConnectionStatus.Checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _agentVersion;

    public MainWindowViewModel(IAgentClient agentClient, string agentUrl)
    {
        _agentClient = agentClient;
        _agentUrl = agentUrl;
    }

    public bool IsOnline => Status == ConnectionStatus.Online;

    public bool IsOffline => Status == ConnectionStatus.Offline;

    public string StatusText => Status switch
    {
        ConnectionStatus.Online => $"Online · agent {AgentVersion}",
        ConnectionStatus.Offline => "Offline",
        _ => "Checking…",
    };

    [RelayCommand]
    public async Task CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        var health = await _agentClient.GetHealthAsync(AgentUrl, cancellationToken);
        AgentVersion = health?.Version;
        Status = health is null ? ConnectionStatus.Offline : ConnectionStatus.Online;
    }

    public async Task RunPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await CheckConnectionAsync(cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }
}
