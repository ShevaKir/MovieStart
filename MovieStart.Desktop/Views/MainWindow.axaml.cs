using Avalonia.Controls;
using MovieStart.Desktop.ViewModels;

namespace MovieStart.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _polling = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
            _ = PollAsync(viewModel);
    }

    protected override void OnClosed(EventArgs e)
    {
        _polling.Cancel();
        base.OnClosed(e);
    }

    private async Task PollAsync(MainWindowViewModel viewModel)
    {
        try
        {
            await viewModel.RunPollingAsync(TimeSpan.FromSeconds(5), _polling.Token);
        }
        catch (OperationCanceledException)
        {
            // Window closed.
        }
    }
}
