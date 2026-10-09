using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieStart.Desktop.Services;
using MovieStart.Shared.Player;

namespace MovieStart.Desktop.ViewModels;

/// <summary>Remote control for the player on the TV.</summary>
public partial class PlayerViewModel : ObservableObject
{
    private const string NothingPlaying = "Nothing playing";

    // Slider drags fire many changes; only the last one is sent.
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(200);

    // After the user moves a slider, ignore polled values until the agent has caught up.
    private static readonly TimeSpan UserChangeGrace = TimeSpan.FromSeconds(1.5);

    private readonly IAgentClient _agentClient;
    private readonly Func<string> _agentUrl;
    private readonly TimeSpan _debounce;

    private bool _applyingState;
    private CancellationTokenSource? _pendingSeek;
    private CancellationTokenSource? _pendingVolume;
    private DateTime _ignorePositionUntil;
    private DateTime _ignoreVolumeUntil;
    private Task _lastSend = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    private bool _isIdle = true;

    [ObservableProperty]
    private string _title = NothingPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _position;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double _duration;

    [ObservableProperty]
    private double _volume = 100;

    [ObservableProperty]
    private IReadOnlyList<TrackOption> _audioTracks = [];

    [ObservableProperty]
    private TrackOption? _selectedAudioTrack;

    [ObservableProperty]
    private IReadOnlyList<TrackOption> _subtitleTracks = [TrackOption.Off];

    [ObservableProperty]
    private TrackOption? _selectedSubtitleTrack = TrackOption.Off;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string? _error;

    public PlayerViewModel(IAgentClient agentClient, Func<string> agentUrl, TimeSpan? debounce = null)
    {
        _agentClient = agentClient;
        _agentUrl = agentUrl;
        _debounce = debounce ?? DefaultDebounce;
    }

    public bool HasMedia => IsConnected && !IsIdle;

    public string PositionText => FormatTime(Position);

    public string DurationText => FormatTime(Duration);

    /// <summary>Completes when the last command sent to the agent has finished. Used by tests.</summary>
    internal Task WhenSentAsync() => _lastSend;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = await _agentClient.GetPlayerStateAsync(_agentUrl(), cancellationToken);
        Apply(state ?? PlayerState.Disconnected);
    }

    public async Task RunPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RefreshAsync(cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    public void Apply(PlayerState state)
    {
        _applyingState = true;
        try
        {
            IsConnected = state.IsConnected;
            IsIdle = state.IsIdle;
            Title = state.IsIdle ? NothingPlaying : state.Title ?? Path.GetFileName(state.FilePath) ?? NothingPlaying;
            IsPaused = state.IsPaused;

            // Duration first: it is the slider maximum.
            Duration = state.Duration;
            if (DateTime.UtcNow >= _ignorePositionUntil)
                Position = state.Position;
            if (DateTime.UtcNow >= _ignoreVolumeUntil)
                Volume = state.Volume;

            List<TrackOption> audio = [.. state.AudioTracks.Select(TrackOption.From)];
            if (!audio.SequenceEqual(AudioTracks))
                AudioTracks = audio;
            SelectedAudioTrack = AudioTracks.FirstOrDefault(track => track.Id == state.AudioTrackId);

            List<TrackOption> subtitles = [TrackOption.Off, .. state.SubtitleTracks.Select(TrackOption.From)];
            if (!subtitles.SequenceEqual(SubtitleTracks))
                SubtitleTracks = subtitles;
            SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(track => track.Id == state.SubtitleTrackId) ?? TrackOption.Off;
        }
        finally
        {
            _applyingState = false;
        }
    }

    [RelayCommand]
    private Task TogglePauseAsync() => SendAsync(IsPaused ? PlayerCommand.Resume() : PlayerCommand.Pause());

    [RelayCommand]
    private Task SeekBackAsync() => SendAsync(PlayerCommand.SeekRelative(-30));

    [RelayCommand]
    private Task SeekForwardAsync() => SendAsync(PlayerCommand.SeekRelative(30));

    [RelayCommand]
    private Task StopAsync() => SendAsync(PlayerCommand.Stop());

    [RelayCommand]
    private async Task PlayFileAsync()
    {
        var result = await _agentClient.PlayAsync(_agentUrl(), FilePath.Trim());
        Error = result.Error;
    }

    partial void OnPositionChanged(double value)
    {
        if (_applyingState)
            return;
        _ignorePositionUntil = DateTime.UtcNow + UserChangeGrace;
        Debounce(ref _pendingSeek, PlayerCommand.SeekAbsolute(value));
    }

    partial void OnVolumeChanged(double value)
    {
        if (_applyingState)
            return;
        _ignoreVolumeUntil = DateTime.UtcNow + UserChangeGrace;
        Debounce(ref _pendingVolume, PlayerCommand.SetVolume((int)Math.Round(value)));
    }

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (!_applyingState && value?.Id is { } id)
            _lastSend = SendAsync(PlayerCommand.SetAudioTrack(id));
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (!_applyingState && value is not null)
            _lastSend = SendAsync(PlayerCommand.SetSubtitleTrack(value.Id));
    }

    private void Debounce(ref CancellationTokenSource? pending, PlayerCommand command)
    {
        pending?.Cancel();
        pending = new CancellationTokenSource();
        _lastSend = SendLaterAsync(command, pending.Token);
    }

    private async Task SendLaterAsync(PlayerCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer value.
            return;
        }

        await SendAsync(command);
    }

    private async Task SendAsync(PlayerCommand command)
    {
        var result = await _agentClient.SendPlayerCommandAsync(_agentUrl(), command);
        Error = result.Error;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
