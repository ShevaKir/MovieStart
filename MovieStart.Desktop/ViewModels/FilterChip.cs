using CommunityToolkit.Mvvm.ComponentModel;

namespace MovieStart.Desktop.ViewModels;

/// <summary>A toggleable filter, e.g. "UKR" or "Dub". Non-generic so views can bind to it.</summary>
public abstract partial class FilterChip(string label, Action changed) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Label { get; } = label;

    partial void OnIsSelectedChanged(bool value) => changed();
}

public sealed class FilterChip<T>(T value, string label, Action changed) : FilterChip(label, changed)
{
    public T Value { get; } = value;
}
