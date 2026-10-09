using System.Collections.ObjectModel;

namespace MovieStart.Desktop.ViewModels;

public interface IKeyed<out TKey>
{
    TKey Key { get; }
}

public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> mirror <paramref name="source"/>, reusing rows by key
    /// so the list does not flicker or lose focus on every poll.
    /// </summary>
    public static void Sync<TViewModel, TModel, TKey>(
        ObservableCollection<TViewModel> target,
        IReadOnlyList<TModel> source,
        Func<TModel, TKey> key,
        Func<TModel, TViewModel> create,
        Action<TViewModel, TModel> update)
        where TViewModel : IKeyed<TKey>
        where TKey : notnull
    {
        var keys = source.Select(key).ToHashSet();
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(target[i].Key))
                target.RemoveAt(i);
        }

        for (var i = 0; i < source.Count; i++)
        {
            var model = source[i];
            var existing = target.FirstOrDefault(vm => EqualityComparer<TKey>.Default.Equals(vm.Key, key(model)));
            if (existing is null)
            {
                existing = create(model);
                target.Insert(i, existing);
            }

            update(existing, model);
            var index = target.IndexOf(existing);
            if (index != i)
                target.Move(index, i);
        }
    }
}
