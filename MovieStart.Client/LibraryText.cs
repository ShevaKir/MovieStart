using MovieStart.Shared.Library;
using MovieStart.Shared.Player;

namespace MovieStart.Client;

/// <summary>Library wording shared by the desktop and web apps.</summary>
public static class LibraryText
{
    /// <summary>"2024 · Series · 18.4 GB".</summary>
    public static string Summary(this LibraryItem item) => string.Join(" · ", new[]
    {
        item.Year?.ToString(),
        item.Kind == MediaKind.Series ? "Series" : "Movie",
        item.Files.Count > 0 ? Format.Size(item.Files.Sum(file => file.SizeBytes)) : null,
    }.Where(part => part is not null));

    /// <summary>First letters of the title; shown until the poster loads, or when there is none.</summary>
    public static string Initials(this LibraryItem item) =>
        string.Concat(item.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0]));

    /// <summary>Label of the main play button, e.g. "Continue S01E02 from 12:30" or "Next S01E03".</summary>
    public static string PlayLabel(this LibraryItem item)
    {
        if (item.ChooseFileToContinue() is not { } next)
            return "Watch";

        var episode = next.EpisodeCode is { } code ? $" {code}" : string.Empty;
        if (next.Id == item.LastPlayedFileId)
            return next.Position > 0 ? $"Continue{episode} from {Format.Duration(next.Position)}" : $"Continue{episode}";

        // The last played episode is finished, so another one is offered.
        return item.LastPlayedFileId is not null && item.Kind == MediaKind.Series ? $"Next{episode}" : $"Watch{episode}";
    }

    /// <summary>The file being continued was stopped partway, so it can also be restarted.</summary>
    public static bool CanStartOver(this LibraryItem item) =>
        item.ChooseFileToContinue() is { Position: > 0 } next && next.Id == item.LastPlayedFileId;

    /// <summary>Short status of downloads that still need attention or time, e.g. "Downloading 42% · 3.1 MB/s"; null when none.</summary>
    public static string? DownloadStatusText(this LibraryItem item)
    {
        var pending = item.Downloads.Where(download => download.Status != DownloadStatus.Ready).ToList();
        if (pending.Count == 0)
            return null;
        if (pending.Count > 1)
            return $"{pending.Count} downloads in progress";

        var download = pending[0];
        return download.Status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.ChoosingFiles => "Waiting for episodes to be chosen",
            DownloadStatus.Downloading => string.Join(" · ", new[]
            {
                $"Downloading {download.Progress * 100:0}%",
                download.DownloadSpeed > 0 ? Format.Speed(download.DownloadSpeed) : null,
                download.EtaSeconds is { } eta ? Format.Eta(eta) : null,
            }.Where(part => part is not null)),
            DownloadStatus.Paused => $"Paused at {download.Progress * 100:0}%",
            _ => download.Error ?? "Download failed",
        };
    }

    /// <summary>"UKR · Dub", or "Track 2" when the track has no language or title.</summary>
    public static string Label(this MediaTrack track)
    {
        var label = string.Join(" · ", new[] { track.Language?.ToUpperInvariant(), track.Title }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return label.Length > 0 ? label : $"Track {track.Id}";
    }
}
