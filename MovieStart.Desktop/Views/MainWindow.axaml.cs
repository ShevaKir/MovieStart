using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MovieStart.Desktop.ViewModels;

namespace MovieStart.Desktop.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PlayerInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LibraryInterval = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan StopOnCloseTimeout = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _polling = new();
    private bool _stoppedPlayback;

    public MainWindow()
    {
        InitializeComponent();
        // Tunnel: handled before a focused button turns Space into a click.
        AddHandler(KeyDownEvent, OnRemoteKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Space plays or pauses, Left and Right seek, while something plays on the TV.</summary>
    private void OnRemoteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || DataContext is not MainWindowViewModel { Player: { HasMedia: true } player })
            return;

        // Typing a search query or choosing a track keeps its keys.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or ComboBox)
            return;

        var command = e.Key switch
        {
            Key.Space => player.TogglePauseCommand,
            Key.Left => player.SeekBackCommand,
            Key.Right => player.SeekForwardCommand,
            _ => null,
        };
        if (command is null)
            return;

        command.Execute(null);
        e.Handled = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = PollAsync(token => viewModel.RunPollingAsync(HealthInterval, token));
            _ = PollAsync(token => viewModel.Player.RunPollingAsync(PlayerInterval, token));
            _ = PollAsync(token => viewModel.Library.RunPollingAsync(LibraryInterval, token));
        }
    }

    /// <summary>Stops the movie on the TV first, so the player does not keep running on the Pi after the app is gone.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _stoppedPlayback || DataContext is not MainWindowViewModel { Player.HasMedia: true } viewModel)
            return;

        e.Cancel = true;
        _stoppedPlayback = true;
        await viewModel.Player.StopIfPlayingAsync(StopOnCloseTimeout);
        Close();
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
