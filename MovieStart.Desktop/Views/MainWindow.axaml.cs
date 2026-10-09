using Avalonia.Controls;
using MovieStart.Desktop.ViewModels;

namespace MovieStart.Desktop.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PlayerInterval = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _polling = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = PollAsync(token => viewModel.RunPollingAsync(HealthInterval, token));
            _ = PollAsync(token => viewModel.Player.RunPollingAsync(PlayerInterval, token));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _polling.Cancel();
        base.OnClosed(e);
    }

    private async Task PollAsync(Func<CancellationToken, Task> poll)
    {
        try
        {
            await poll(_polling.Token);
        }
        catch (OperationCanceledException)
        {
            // Window closed.
        }
    }
}
