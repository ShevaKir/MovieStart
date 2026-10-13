using MovieStart.Client;
using MovieStart.Shared.Library;

namespace MovieStart.Web.State;

/// <summary>The library on the Pi and what can be done with it.</summary>
public sealed class LibraryStore(IAgentClient agent, AgentAddress address, Notifications notifications)
    : Store(TimeSpan.FromSeconds(5))
{
    public IReadOnlyList<LibraryItem> Items { get; private set; } = [];

    public StorageInfo? Storage { get; private set; }

    /// <summary>False when the last refresh could not reach the agent.</summary>
    public bool IsOnline { get; private set; } = true;

    public LibraryItem? Find(Guid itemId) => Items.FirstOrDefault(item => item.Id == itemId);

    public async Task PlayAsync(Guid itemId, PlayItemRequest request)
    {
        var result = await agent.PlayItemAsync(address.Url, itemId, request);
        if (notifications.Report(result))
            await RefreshAsync();
    }

    public async Task DeleteItemAsync(Guid itemId)
    {
        if (notifications.Report(await agent.DeleteItemAsync(address.Url, itemId)))
            await RefreshAsync();
    }

    public async Task DeleteFileAsync(Guid itemId, int fileId)
    {
        if (notifications.Report(await agent.DeleteFileAsync(address.Url, itemId, fileId)))
            await RefreshAsync();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var items = await agent.GetLibraryAsync(address.Url, cancellationToken);
        IsOnline = items is not null;
        if (items is null)
            return;

        Items = items;
        Storage = await agent.GetStorageAsync(address.Url, cancellationToken) ?? Storage;
    }
}
