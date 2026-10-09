using MovieStart.Agent.Media;
using MovieStart.Agent.Player;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Demo;

/// <summary>
/// Stands in for mpv: keeps a playback clock and the real track list of the file (from ffprobe),
/// and answers every remote command the way mpv would.
/// </summary>
public sealed class DemoPlayer(IMediaProbe probe, TimeProvider time) : BackgroundService, IPlayer
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private PlayerState _state = new() { IsConnected = true, IsIdle = true, Volume = 100 };
    private DateTimeOffset _lastTick;

    public PlayerState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    public async Task PlayAsync(string path, PlaybackOptions options, CancellationToken cancellationToken)
    {
        var info = await probe.ProbeAsync(path, cancellationToken);
        var audio = info?.Audio ?? [];
        var subtitles = info?.Subtitles ?? [];

        lock (_lock)
        {
            _state = _state with
            {
                IsIdle = false,
                IsPaused = false,
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Duration = info?.Duration ?? 0,
                Position = options.StartSeconds,
                AudioTracks = audio,
                SubtitleTracks = subtitles,
                // Like mpv: the requested track, otherwise the first one.
                AudioTrackId = options.AudioTrackId ?? audio.FirstOrDefault()?.Id,
                SubtitleTrackId = options.Subtitles?.TrackId,
            };
            _lastTick = time.GetUtcNow();
        }
    }

    public Task SendAsync(PlayerCommand command, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_state.IsIdle && command.Type != PlayerCommandType.SetVolume && command.Type != PlayerCommandType.Stop)
                throw new PlayerCommandException(command.Type.ToString(), "property unavailable");

            _state = command.Type switch
            {
                PlayerCommandType.Pause => _state with { IsPaused = true },
                PlayerCommandType.Resume => _state with { IsPaused = false },
                PlayerCommandType.SeekRelative => _state with { Position = Clamp(_state.Position + Require(command)) },
                PlayerCommandType.SeekAbsolute => _state with { Position = Clamp(Require(command)) },
                PlayerCommandType.SetVolume => _state with { Volume = (int)Math.Clamp(Require(command), 0, 100) },
                PlayerCommandType.SetAudioTrack => _state with { AudioTrackId = Track(_state.AudioTracks, (int)Require(command)) },
                PlayerCommandType.SetSubtitleTrack => _state with
                {
                    SubtitleTrackId = command.Value is { } id ? Track(_state.SubtitleTracks, (int)id) : null,
                },
                PlayerCommandType.Stop => Idle(_state),
                _ => throw new ArgumentException($"Unknown command '{command.Type}'."),
            };
            _lastTick = time.GetUtcNow();
        }

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            lock (_lock)
            {
                var now = time.GetUtcNow();
                var elapsed = (now - _lastTick).TotalSeconds;
                _lastTick = now;
                if (_state.IsIdle || _state.IsPaused)
                    continue;

                var position = _state.Position + elapsed;
                // mpv goes idle at the end of the file.
                _state = _state.Duration > 0 && position >= _state.Duration ? Idle(_state) : _state with { Position = position };
            }
        }
    }

    private double Clamp(double position) => Math.Clamp(position, 0, _state.Duration > 0 ? _state.Duration : double.MaxValue);

    private static PlayerState Idle(PlayerState state) => state with
    {
        IsIdle = true,
        IsPaused = false,
        FilePath = null,
        Title = null,
        Position = 0,
        Duration = 0,
        AudioTracks = [],
        SubtitleTracks = [],
        AudioTrackId = null,
        SubtitleTrackId = null,
    };

    private static int Track(IReadOnlyList<MediaTrack> tracks, int id) =>
        tracks.Any(track => track.Id == id) ? id : throw new PlayerCommandException("set_property", $"no track {id}");

    private static double Require(PlayerCommand command) =>
        command.Value ?? throw new ArgumentException($"Command '{command.Type}' requires a value.");
}
