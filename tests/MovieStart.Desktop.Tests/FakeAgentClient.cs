using MovieStart.Desktop.Services;
using MovieStart.Shared.Health;
using MovieStart.Shared.Library;
using MovieStart.Shared.Player;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Desktop.Tests;

public sealed class FakeAgentClient : IAgentClient
{
    public HealthResponse? Health { get; set; }

    public PlayerState? PlayerState { get; set; }

    public AgentResult Result { get; set; } = AgentResult.Success;

    public string? LastAgentUrl { get; private set; }

    public List<LibraryItem>? Library { get; set; } = [];

    public StorageInfo? Storage { get; set; }

    public List<string> Calls { get; } = [];

    public AddToLibraryRequest? Added { get; private set; }

    public PlayItemRequest? PlayRequest { get; private set; }

    public List<TitleResult> Titles { get; set; } = [];

    public List<ReleaseInfo> Releases { get; set; } = [];

    public string? ReleaseQuery { get; private set; }

    public VoiceProfile Profile { get; set; } = VoiceProfile.Default;

    public VoiceProfile? SavedProfile { get; private set; }

    public List<PlayerCommand> Commands { get; } = [];

    public Task<HealthResponse?> GetHealthAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        return Task.FromResult(Health);
    }

    public Task<PlayerState?> GetPlayerStateAsync(string agentUrl, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        return Task.FromResult(PlayerState);
    }

    public Task<AgentResult> SendPlayerCommandAsync(string agentUrl, PlayerCommand command, CancellationToken cancellationToken = default)
    {
        LastAgentUrl = agentUrl;
        Commands.Add(command);
        return Task.FromResult(Result);
    }

    public Task<IReadOnlyList<LibraryItem>?> GetLibraryAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LibraryItem>?>(Library);

    public Task<StorageInfo?> GetStorageAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(Storage);

    public Task<AgentResult> AddToLibraryAsync(string agentUrl, AddToLibraryRequest request, CancellationToken cancellationToken = default)
    {
        Added = request;
        return Record("add");
    }

    public Task<AgentResult> DeleteItemAsync(string agentUrl, Guid itemId, CancellationToken cancellationToken = default) =>
        Record($"delete {itemId}");

    public Task<AgentResult> DeleteDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        Record($"delete {itemId}/{downloadId}");

    public Task<AgentResult> SelectDownloadFilesAsync(
        string agentUrl, Guid itemId, Guid downloadId, SelectFilesRequest request, CancellationToken cancellationToken = default) =>
        Record($"select {downloadId} {string.Join(',', request.Wanted)}");

    public Task<AgentResult> DeleteFileAsync(string agentUrl, Guid itemId, int fileId, CancellationToken cancellationToken = default) =>
        Record($"delete file {fileId}");

    public Task<AgentResult> PauseDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        Record($"pause {downloadId}");

    public Task<AgentResult> ResumeDownloadAsync(string agentUrl, Guid itemId, Guid downloadId, CancellationToken cancellationToken = default) =>
        Record($"resume {downloadId}");

    public Task<AgentResult<MediaFile>> PlayItemAsync(
        string agentUrl, Guid itemId, PlayItemRequest request, CancellationToken cancellationToken = default)
    {
        PlayRequest = request;
        Calls.Add($"play {itemId}");
        return Task.FromResult(new AgentResult<MediaFile>(null, Result.Error));
    }

    public Task<AgentResult<IReadOnlyList<TitleResult>>> SearchTitlesAsync(
        string agentUrl, string query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentResult<IReadOnlyList<TitleResult>>(Result.IsSuccess ? Titles : null, Result.Error));

    public Task<AgentResult<IReadOnlyList<ReleaseInfo>>> SearchReleasesAsync(
        string agentUrl, TitleResult title, string? query, CancellationToken cancellationToken = default)
    {
        ReleaseQuery = query;
        return Task.FromResult(new AgentResult<IReadOnlyList<ReleaseInfo>>(Result.IsSuccess ? Releases : null, Result.Error));
    }

    public Task<AgentResult<VoiceProfile>> GetVoiceProfileAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentResult<VoiceProfile>(Result.IsSuccess ? Profile : null, Result.Error));

    public Task<AgentResult> SaveVoiceProfileAsync(string agentUrl, VoiceProfile profile, CancellationToken cancellationToken = default)
    {
        SavedProfile = profile;
        return Record("save profile");
    }

    public Task<AgentResult> CompleteDemoDownloadsAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        Record("demo complete");

    public Task<AgentResult> ResetDemoAsync(string agentUrl, CancellationToken cancellationToken = default) =>
        Record("demo reset");

    private Task<AgentResult> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(Result);
    }
}
