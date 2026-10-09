namespace MovieStart.Agent.Downloads;

public sealed class QbitOptions
{
    public const string SectionName = "QBittorrent";

    /// <summary>qBittorrent WebUI address.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    public string Username { get; set; } = "admin";

    public string Password { get; set; } = string.Empty;
}
