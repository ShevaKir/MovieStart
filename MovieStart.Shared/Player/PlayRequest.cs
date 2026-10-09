namespace MovieStart.Shared.Player;

/// <param name="Path">Absolute path of a media file on the agent host, inside the configured media root.</param>
public sealed record PlayRequest(string Path);
