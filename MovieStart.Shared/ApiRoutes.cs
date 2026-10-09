namespace MovieStart.Shared;

public static class ApiRoutes
{
    public const string Health = "/api/health";

    public const string PlayerState = "/api/player/state";
    public const string PlayerPlay = "/api/player/play";
    public const string PlayerCommand = "/api/player/command";

    public const string Library = "/api/library";
    public const string Storage = "/api/storage";

    public const string SearchTitles = "/api/search/titles";
    public const string SearchReleases = "/api/search/releases";

    public const string VoiceProfile = "/api/profile/voice";

    /// <summary>Only in demo mode: finish every simulated download now.</summary>
    public const string DemoCompleteDownloads = "/api/demo/complete-downloads";

    /// <summary>Only in demo mode: delete the demo library and start over.</summary>
    public const string DemoReset = "/api/demo/reset";

    public static string LibraryItem(Guid id) => $"{Library}/{id}";

    public static string LibraryPlay(Guid id) => $"{Library}/{id}/play";

    public static string LibraryDownload(Guid id, Guid downloadId) => $"{Library}/{id}/downloads/{downloadId}";

    public static string LibraryDownloadPause(Guid id, Guid downloadId) => $"{LibraryDownload(id, downloadId)}/pause";

    public static string LibraryDownloadResume(Guid id, Guid downloadId) => $"{LibraryDownload(id, downloadId)}/resume";
}
