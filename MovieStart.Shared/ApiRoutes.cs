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

    /// <summary>The item's poster, stored on the Pi.</summary>
    public static string LibraryPoster(Guid id) => $"{Library}/{id}/poster";

    public static string LibraryDownload(Guid id, Guid downloadId) => $"{Library}/{id}/downloads/{downloadId}";

    public static string LibraryDownloadPause(Guid id, Guid downloadId) => $"{LibraryDownload(id, downloadId)}/pause";

    public static string LibraryDownloadResume(Guid id, Guid downloadId) => $"{LibraryDownload(id, downloadId)}/resume";

    /// <summary>PUT: choose which files of the torrent to download.</summary>
    public static string LibraryDownloadFiles(Guid id, Guid downloadId) => $"{LibraryDownload(id, downloadId)}/files";

    /// <summary>DELETE: remove one downloaded file, e.g. a watched episode.</summary>
    public static string LibraryFile(Guid id, int fileId) => $"{Library}/{id}/files/{fileId}";
}
