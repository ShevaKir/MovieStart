using Avalonia.Media.Imaging;
using MovieStart.Desktop.Services;

namespace MovieStart.Desktop.Tests;

public sealed class NoPosters : IPosterLoader
{
    public Task<Bitmap?> LoadAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult<Bitmap?>(null);
}
