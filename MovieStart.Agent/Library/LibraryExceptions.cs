namespace MovieStart.Agent.Library;

public sealed class LibraryNotFoundException(string message) : Exception(message);

/// <summary>The item exists but is not in a state that allows the operation, e.g. playing before anything is downloaded.</summary>
public sealed class LibraryStateException(string message) : Exception(message);

public sealed class InsufficientStorageException(long requiredBytes, long availableBytes)
    : Exception($"Not enough space: {FormatGb(requiredBytes)} needed, {FormatGb(availableBytes)} available.")
{
    private static string FormatGb(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";
}
