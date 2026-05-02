using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace ComCross.Shell.ViewModels;

public sealed class ItemVmCollection<TVm, TContext> :
    IReadOnlyList<TVm>,
    IList,
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IDisposable
    where TVm : notnull, IInitializable<TContext>
{
    private readonly IItemVmFactory<TVm, TContext> _factory;
    private readonly ObservableCollection<TVm> _items = new();
    private bool _isDisposed;

    public ItemVmCollection(IItemVmFactory<TVm, TContext> factory)
    {
        _factory = factory;
        _items.CollectionChanged += OnInnerCollectionChanged;
        ((INotifyPropertyChanged)_items).PropertyChanged += OnInnerPropertyChanged;
    }

    public int Count => _items.Count;

    public TVm this[int index] => _items[index];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TVm Add(TContext context)
    {
        var item = _factory.Create(context);
        _items.Add(item);
        return item;
    }

    public TVm Insert(int index, TContext context)
    {
        var item = _factory.Create(context);
        _items.Insert(index, item);
        return item;
    }

    public bool Remove(TVm item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        DisposeItem(item);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        DisposeItem(item);
    }

    public TVm ReplaceAt(int index, TContext context)
    {
        var oldItem = _items[index];
        var newItem = _factory.Create(context);
        _items[index] = newItem;
        DisposeItem(oldItem);
        return newItem;
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var oldItems = _items.ToArray();

        _items.Clear();

        DisposeItems(oldItems);
    }

    public IEnumerator<TVm> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    int ICollection.Count => _items.Count;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    object? IList.this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException($"{nameof(ItemVmCollection<TVm, TContext>)} is read-only via IList.");
    }

    int IList.Add(object? value)
        => throw new NotSupportedException($"Use {nameof(Add)}(TContext) to add items.");

    void IList.Clear()
        => throw new NotSupportedException($"Use {nameof(Clear)}() to clear items.");

    bool IList.Contains(object? value)
        => value is TVm item && _items.Contains(item);

    int IList.IndexOf(object? value)
        => value is TVm item ? _items.IndexOf(item) : -1;

    void IList.Insert(int index, object? value)
        => throw new NotSupportedException($"Use {nameof(Insert)}(int, TContext) to insert items.");

    void IList.Remove(object? value)
        => throw new NotSupportedException($"Use {nameof(Remove)}({nameof(TVm)}) to remove items.");

    void IList.RemoveAt(int index)
        => throw new NotSupportedException($"Use {nameof(RemoveAt)}(int) to remove items.");

    void ICollection.CopyTo(Array array, int index)
        => ((ICollection)_items).CopyTo(array, index);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (disposing)
        {
            Clear();
            _items.CollectionChanged -= OnInnerCollectionChanged;
            ((INotifyPropertyChanged)_items).PropertyChanged -= OnInnerPropertyChanged;
        }
    }

    private void OnInnerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    private void OnInnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, e);

    private static void DisposeItems(IEnumerable items)
    {
        foreach (var item in items)
        {
            DisposeItem(item);
        }
    }

    private static void DisposeItem(object? item)
    {
        if (item is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
