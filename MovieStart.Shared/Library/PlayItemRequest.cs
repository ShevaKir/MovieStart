namespace MovieStart.Shared.Library;

/// <param name="FileId">File to play; null continues where the viewer stopped (next episode if that one is finished).</param>
/// <param name="FromStart">Ignore the saved position.</param>
public sealed record PlayItemRequest(int? FileId = null, bool FromStart = false);
