using System.Globalization;

namespace MovieStart.Desktop.ViewModels;

public static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0 GB", Culture),
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0 MB", Culture),
        _ => (bytes / 1024d).ToString("0 KB", Culture),
    };

    public static string Speed(long bytesPerSecond) => bytesPerSecond >= 1L << 20
        ? (bytesPerSecond / (double)(1L << 20)).ToString("0.0 MB/s", Culture)
        : (bytesPerSecond / 1024d).ToString("0 KB/s", Culture);

    public static string Duration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", Culture) : time.ToString(@"m\:ss", Culture);
    }

    public static string Eta(long seconds) => seconds switch
    {
        < 60 => "less than a minute left",
        < 3600 => $"{seconds / 60} min left",
        _ => $"{seconds / 3600} h {seconds % 3600 / 60} min left",
    };
}
