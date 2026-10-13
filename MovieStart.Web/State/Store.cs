namespace MovieStart.Web.State;

/// <summary>
/// Agent data that pages share. Refreshes itself on an interval while the app is open
/// and tells subscribed components to render again.
/// </summary>
public abstract class Store(TimeSpan interval) : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private Task? _polling;

    public event Action? Changed;

    /// <summary>True once the first refresh has finished, successfully or not.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Starts refreshing; later calls do nothing.</summary>
    public void Start() => _polling ??= PollAsync(_disposed.Token);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        IsLoaded = true;
        NotifyChanged();
    }

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    protected void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                await RefreshAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
