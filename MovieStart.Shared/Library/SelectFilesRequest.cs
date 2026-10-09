namespace MovieStart.Shared.Library;

/// <summary>Files of a download to fetch; every other file of the torrent is skipped.</summary>
/// <param name="Wanted">Torrent file indexes (<see cref="DownloadFile.Index"/>).</param>
public sealed record SelectFilesRequest(IReadOnlyList<int> Wanted);
