namespace MovieStart.Agent.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Directory where movies are downloaded. Only files inside it may be played.</summary>
    public string Root { get; set; } = "/mnt/media/movies";

    /// <summary>ffprobe executable (from ffmpeg) used to read audio and subtitle tracks.</summary>
    public string FfprobePath { get; set; } = "ffprobe";
}
