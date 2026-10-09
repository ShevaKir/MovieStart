using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MovieStart.Agent.Tests;

/// <summary>Minimal stand-in for mpv's JSON IPC server: records commands and answers "success".</summary>
public sealed class FakeMpvServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentDictionary<string, string> _errors = new();
    private Socket? _connection;

    public FakeMpvServer()
    {
        // Unix socket paths are limited to ~104 bytes on macOS, so keep the name short.
        SocketPath = Path.Combine(Path.GetTempPath(), $"mpv-{Guid.NewGuid().ToString("N")[..8]}.sock");
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string SocketPath { get; }

    public ConcurrentQueue<JsonElement[]> Commands { get; } = new();

    /// <summary>Makes mpv answer the given command name with an error.</summary>
    public void FailCommand(string name, string error) => _errors[name] = error;

    public Task SendEventAsync(object message) => WriteLineAsync(JsonSerializer.Serialize(message));

    public Task SendPropertyChangeAsync(string name, object? data) =>
        SendEventAsync(new { @event = "property-change", id = 1, name, data });

    public void DropConnection() => _connection?.Close();

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _connection = await _listener.AcceptAsync(_stop.Token);
                await ServeAsync(_connection);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync(Socket connection)
    {
        using var stream = new NetworkStream(connection, ownsSocket: false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        try
        {
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var command = root.GetProperty("command").EnumerateArray().Select(e => e.Clone()).ToArray();
                Commands.Enqueue(command);

                var error = _errors.GetValueOrDefault(command[0].GetString()!, "success");
                await WriteLineAsync(JsonSerializer.Serialize(new
                {
                    request_id = root.GetProperty("request_id").GetInt64(),
                    error,
                }));
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task WriteLineAsync(string json)
    {
        var connection = _connection ?? throw new InvalidOperationException("No client connected.");
        await connection.SendAsync(Encoding.UTF8.GetBytes(json + "\n"));
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _connection?.Dispose();
        _listener.Dispose();
        await _acceptLoop;
        File.Delete(SocketPath);
    }
}
