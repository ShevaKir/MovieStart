namespace MovieStart.Shared.Player;

/// <param name="Language">ISO 639-2 code as reported by the container, e.g. "ukr", "rus", "eng".</param>
/// <param name="Title">Track title from the container, e.g. "Dub, Blu-ray CEE".</param>
public sealed record MediaTrack(int Id, string? Language, string? Title);
