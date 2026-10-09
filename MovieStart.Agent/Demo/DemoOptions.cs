namespace MovieStart.Agent.Demo;

/// <summary>
/// Demo mode: TMDB stays real, while Prowlarr, qBittorrent and mpv are simulated, so the whole app
/// can be tried on a laptop. Finished downloads are real, small video files made by ffmpeg.
/// </summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    /// <summary>How long a simulated download takes.</summary>
    public int DownloadSeconds { get; set; } = 25;

    /// <summary>Length of the generated video clips.</summary>
    public int ClipSeconds { get; set; } = 300;

    public string FfmpegPath { get; set; } = "ffmpeg";
}
