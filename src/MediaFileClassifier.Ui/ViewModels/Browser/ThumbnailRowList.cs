using System.Collections;
using System.Collections.Specialized;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The grid's rows as a list that makes rows only when the panel asks for them (E14-S01-T02), so 100k results cost
/// nothing until they're on screen. Rows are kept while attached and a few more, then dropped.
/// </summary>
public sealed class ThumbnailRowList : IList, IReadOnlyList<ThumbnailRowViewModel>, INotifyCollectionChanged
{
    private const int KeptRows = 256;

    private readonly Func<int, ThumbnailRowViewModel> create;
    private readonly Dictionary<int, ThumbnailRowViewModel> rows = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailRowList"/> class.
    /// </summary>
    /// <param name="create">Makes a row.</param>
    public ThumbnailRowList(Func<int, ThumbnailRowViewModel> create)
    {
        this.create = create;
    }

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public int Count { get; private set; }

    /// <inheritdoc />
    bool IList.IsFixedSize => false;

    /// <inheritdoc />
    bool IList.IsReadOnly => true;

    /// <inheritdoc />
    bool ICollection.IsSynchronized => false;

    /// <inheritdoc />
    object ICollection.SyncRoot => this;

    /// <inheritdoc />
    public ThumbnailRowViewModel this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            if (!rows.TryGetValue(index, out var row))
            {
                if (rows.Count >= KeptRows)
                {
                    foreach (var unused in rows.Where(pair => !pair.Value.IsAttached).Select(pair => pair.Key).ToList())
                    {
                        rows.Remove(unused);
                    }
                }

                row = create(index);
                rows[index] = row;
            }

            return row;
        }
    }

    /// <inheritdoc />
    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Replaces every row, e.g. after the column count, tile size or results changed. Attached rows are detached by
    /// their views as the panel recycles them.
    /// </summary>
    /// <param name="count">The new number of rows.</param>
    public void Reset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        rows.Clear();
        Count = count;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <inheritdoc />
    public int IndexOf(object? value) => value is ThumbnailRowViewModel row && row.Index < Count && rows.TryGetValue(row.Index, out var held) && held == row ? row.Index : -1;

    /// <inheritdoc />
    public bool Contains(object? value) => IndexOf(value) >= 0;

    /// <inheritdoc />
    public IEnumerator<ThumbnailRowViewModel> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    void ICollection.CopyTo(Array array, int index) => throw new NotSupportedException();

    /// <inheritdoc />
    int IList.Add(object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    void IList.Clear() => throw new NotSupportedException();

    /// <inheritdoc />
    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    void IList.Remove(object? value) => throw new NotSupportedException();

    /// <inheritdoc />
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
