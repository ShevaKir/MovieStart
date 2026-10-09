using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Player;

/// <summary>
/// Controls an mpv instance started separately (systemd on the Pi) with <c>--idle --input-ipc-server</c>.
/// Keeps reconnecting while the agent runs and mirrors mpv properties into <see cref="State"/>.
/// </summary>
public sealed class MpvPlayer(IOptions<PlayerOptions> options, ILogger<MpvPlayer> logger) : BackgroundService, IPlayer
{
    private static readonly string[] ObservedProperties =
        ["idle-active", "path", "media-title", "pause", "time-pos", "duration", "volume", "aid", "sid", "track-list"];

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _stateLock = new();
    private PlayerState _state = PlayerState.Disconnected;
    private volatile MpvIpcClient? _client;

    public PlayerState State
    {
        get
        {
            lock (_stateLock)
                return _state;
        }
    }

    public async Task PlayAsync(string path, CancellationToken cancellationToken)
    {
        await SendMpvAsync(["loadfile", path, "replace"], cancellationToken);
        await SendMpvAsync(["set_property", "pause", false], cancellationToken);
    }

    public Task SendAsync(PlayerCommand command, CancellationToken cancellationToken) =>
        SendMpvAsync(ToMpvCommand(command), cancellationToken);

    internal static object?[] ToMpvCommand(PlayerCommand command) => command.Type switch
    {
        PlayerCommandType.Pause => ["set_property", "pause", true],
        PlayerCommandType.Resume => ["set_property", "pause", false],
        PlayerCommandType.SeekRelative => ["seek", RequireValue(command), "relative"],
        PlayerCommandType.SeekAbsolute => ["seek", RequireValue(command), "absolute"],
        PlayerCommandType.SetVolume => ["set_property", "volume", Math.Clamp(RequireValue(command), 0, 100)],
        PlayerCommandType.SetAudioTrack => ["set_property", "aid", (int)RequireValue(command)],
        PlayerCommandType.SetSubtitleTrack => command.Value is { } id
            ? ["set_property", "sid", (int)id]
            : ["set_property", "sid", "no"],
        PlayerCommandType.Stop => ["stop"],
        _ => throw new ArgumentException($"Unknown command '{command.Type}'."),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reportedUnavailable = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var client = await MpvIpcClient.ConnectAsync(options.Value.SocketPath, stoppingToken);
                client.EventReceived += OnEvent;
                SetState(PlayerState.Disconnected with { IsConnected = true });

                for (var i = 0; i < ObservedProperties.Length; i++)
                    await client.SendAsync(["observe_property", i + 1, ObservedProperties[i]], stoppingToken);

                _client = client;
                reportedUnavailable = false;
                logger.LogInformation("Connected to mpv at {SocketPath}", options.Value.SocketPath);

                await client.Completion.WaitAsync(stoppingToken);
                logger.LogWarning("Lost connection to mpv");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is SocketException or IOException or PlayerCommandException)
            {
                if (!reportedUnavailable)
                    logger.LogWarning("mpv is not available at {SocketPath}: {Reason}", options.Value.SocketPath, ex.Message);
                reportedUnavailable = true;
            }
            finally
            {
                _client = null;
                SetState(PlayerState.Disconnected);
            }

            try
            {
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SendMpvAsync(object?[] command, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new PlayerUnavailableException();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            await client.SendAsync(command, timeout.Token);
        }
        catch (IOException)
        {
            throw new PlayerUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // mpv did not answer in time.
            throw new PlayerUnavailableException();
        }
    }

    private void OnEvent(JsonElement message)
    {
        if (message.GetProperty("event").GetString() != "property-change")
            return;

        // "data" is absent when the property is unavailable, e.g. time-pos while idle.
        var data = message.TryGetProperty("data", out var value) ? value : default;
        var name = message.GetProperty("name").GetString();

        lock (_stateLock)
        {
            _state = name switch
            {
                "idle-active" => _state with { IsIdle = data.ValueKind == JsonValueKind.True },
                "path" => _state with { FilePath = AsString(data) },
                "media-title" => _state with { Title = AsString(data) },
                "pause" => _state with { IsPaused = data.ValueKind == JsonValueKind.True },
                "time-pos" => _state with { Position = AsNumber(data) },
                "duration" => _state with { Duration = AsNumber(data) },
                "volume" => _state with { Volume = (int)Math.Round(AsNumber(data)) },
                "aid" => _state with { AudioTrackId = AsTrackId(data) },
                "sid" => _state with { SubtitleTrackId = AsTrackId(data) },
                "track-list" => _state with
                {
                    AudioTracks = AsTracks(data, "audio"),
                    SubtitleTracks = AsTracks(data, "sub"),
                },
                _ => _state,
            };
        }
    }

    private void SetState(PlayerState state)
    {
        lock (_stateLock)
            _state = state;
    }

    private static string? AsString(JsonElement data) =>
        data.ValueKind == JsonValueKind.String ? data.GetString() : null;

    private static double AsNumber(JsonElement data) =>
        data.ValueKind == JsonValueKind.Number ? data.GetDouble() : 0;

    // mpv reports a disabled track as false ("no").
    private static int? AsTrackId(JsonElement data) =>
        data.ValueKind == JsonValueKind.Number ? data.GetInt32() : null;

    private static IReadOnlyList<MediaTrack> AsTracks(JsonElement data, string type)
    {
        if (data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Where(track => track.TryGetProperty("type", out var t) && t.GetString() == type)
            .Select(track => new MediaTrack(
                track.GetProperty("id").GetInt32(),
                track.TryGetProperty("lang", out var lang) ? AsString(lang) : null,
                track.TryGetProperty("title", out var title) ? AsString(title) : null))
            .ToList();
    }

    private static double RequireValue(PlayerCommand command) =>
        command.Value ?? throw new ArgumentException($"Command '{command.Type}' requires a value.");
}
