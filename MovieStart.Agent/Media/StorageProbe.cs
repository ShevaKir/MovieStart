using MovieStart.Shared.Library;

namespace MovieStart.Agent.Media;

public interface IStorageProbe
{
    StorageInfo Measure(string path);
}

public sealed class DriveStorageProbe : IStorageProbe
{
    public StorageInfo Measure(string path)
    {
        var drive = new DriveInfo(path);
        return new StorageInfo(drive.TotalSize, drive.AvailableFreeSpace);
    }
}
