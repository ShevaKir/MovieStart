using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MovieStart.Desktop.ViewModels;

public static class ViewConverters
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#18794E"));
    private static readonly IBrush Border = new SolidColorBrush(Color.Parse("#E4E2DD"));

    /// <summary>Green border for the recommended release.</summary>
    public static IValueConverter BestBorder { get; } =
        new FuncValueConverter<bool, IBrush>(isBest => isBest ? Accent : Border);
}
