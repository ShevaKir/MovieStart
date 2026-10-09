namespace MovieStart.Agent.Player;

public sealed class PlayerOptions
{
    public const string SectionName = "Player";

    /// <summary>mpv JSON IPC socket (<c>--input-ipc-server</c>).</summary>
    public string SocketPath { get; set; } = "/run/mpv/mpv.sock";

    /// <summary>Only files inside this directory may be played.</summary>
    public string MediaRoot { get; set; } = "/mnt/movies";
}
