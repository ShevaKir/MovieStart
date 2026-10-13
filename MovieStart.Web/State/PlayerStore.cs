using MovieStart.Client;
using MovieStart.Shared.Player;

namespace MovieStart.Web.State;

/// <summary>What the TV is playing, and the remote control.</summary>
public sealed class PlayerStore(IAgentClient agent, AgentAddress address, Notifications notifications)
    : Store(TimeSpan.FromSeconds(1))
{
    public const double SeekStepSeconds = 30;

    public PlayerState State { get; private set; } = PlayerState.Disconnected;

    public bool HasMedia => State is { IsConnected: true, IsIdle: false };

    public Task TogglePauseAsync() => SendAsync(State.IsPaused ? PlayerCommand.Resume() : PlayerCommand.Pause());

    public Task SeekByAsync(double seconds) => SendAsync(PlayerCommand.SeekRelative(seconds));

    public Task SeekToAsync(double seconds) => SendAsync(PlayerCommand.SeekAbsolute(seconds));

    public Task SetVolumeAsync(int volume) => SendAsync(PlayerCommand.SetVolume(volume));

    public Task SetAudioTrackAsync(int trackId) => SendAsync(PlayerCommand.SetAudioTrack(trackId));

    public Task SetSubtitleTrackAsync(int? trackId) => SendAsync(PlayerCommand.SetSubtitleTrack(trackId));

    public Task StopAsync() => SendAsync(PlayerCommand.Stop());

    private async Task SendAsync(PlayerCommand command)
    {
        if (notifications.Report(await agent.SendPlayerCommandAsync(address.Url, command)))
            await RefreshAsync();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken) =>
        State = await agent.GetPlayerStateAsync(address.Url, cancellationToken) ?? PlayerState.Disconnected;
}
