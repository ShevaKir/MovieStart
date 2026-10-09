using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MovieStart.Agent.Player;

/// <summary>Line-delimited JSON IPC connection to mpv (https://mpv.io/manual/stable/#json-ipc).</summary>
public sealed class MpvIpcClient : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _disposed = new();
    private readonly Task _readLoop;
    private long _nextRequestId;

    private MpvIpcClient(Socket socket)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Raised on the reader thread for every mpv event, e.g. <c>property-change</c>.</summary>
    public event Action<JsonElement>? EventReceived;

    /// <summary>Completes when the connection is closed.</summary>
    public Task Completion => _readLoop;

    public static async Task<MpvIpcClient> ConnectAsync(string socketPath, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
            return new MpvIpcClient(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends a command and returns its <c>data</c>.</summary>
    /// <exception cref="PlayerCommandException">mpv answered with an error.</exception>
    /// <exception cref="IOException">The connection is closed.</exception>
    public async Task<JsonElement> SendAsync(object?[] command, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = reply;
        try
        {
            if (_readLoop.IsCompleted)
                throw new IOException("The mpv connection is closed.");

            var line = JsonSerializer.SerializeToUtf8Bytes(new { command, request_id = requestId });
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _stream.WriteAsync(line, cancellationToken);
                await _stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            var response = await reply.Task.WaitAsync(cancellationToken);
            var error = response.TryGetProperty("error", out var e) ? e.GetString() : null;
            if (error != "success")
                throw new PlayerCommandException(command[0]?.ToString() ?? "?", error);

            return response.TryGetProperty("data", out var data) ? data : default;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            while (await reader.ReadLineAsync(_disposed.Token) is { } line)
            {
                if (TryParse(line) is not { } message)
                    continue;

                if (message.TryGetProperty("request_id", out var id)
                    && id.ValueKind == JsonValueKind.Number
                    && _pending.TryGetValue(id.GetInt64(), out var reply))
                {
                    reply.TrySetResult(message);
                }
                else if (message.TryGetProperty("event", out _))
                {
                    EventReceived?.Invoke(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Connection closed; pending requests are failed below.
        }
        finally
        {
            foreach (var reply in _pending.Values)
                reply.TrySetException(new IOException("The mpv connection is closed."));
        }
    }

    private static JsonElement? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Already disconnected by the other side.
        }

        _socket.Dispose();
        await _readLoop;
        await _stream.DisposeAsync();
        _disposed.Dispose();
        _writeLock.Dispose();
    }
}
