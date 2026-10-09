namespace MovieStart.Agent.Player;

public static class MediaPath
{
    /// <summary>Resolves <paramref name="path"/> and checks that it stays inside <paramref name="mediaRoot"/>.</summary>
    public static bool TryResolve(string mediaRoot, string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return false;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot)) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(path);
        if (!resolved.StartsWith(root, StringComparison.Ordinal))
            return false;

        fullPath = resolved;
        return true;
    }
}
