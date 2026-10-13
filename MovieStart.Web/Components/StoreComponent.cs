using Microsoft.AspNetCore.Components;
using MovieStart.Web.State;

namespace MovieStart.Web.Components;

/// <summary>Renders again whenever one of the stores it watches changes.</summary>
public abstract class StoreComponent : ComponentBase, IDisposable
{
    private Store[] _stores = [];

    protected void Watch(params Store[] stores)
    {
        _stores = stores;
        foreach (var store in stores)
            store.Changed += OnStoreChanged;
    }

    private void OnStoreChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Changed -= OnStoreChanged;
        GC.SuppressFinalize(this);
    }
}
