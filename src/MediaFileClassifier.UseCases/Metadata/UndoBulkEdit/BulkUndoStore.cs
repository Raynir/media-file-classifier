using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;

/// <summary>
/// Holds what the last bulk edit changed, so Undo can put it back (E07-S03-T03, D038: undo is kept in memory for the
/// session). It keeps one operation: a new bulk edit replaces the earlier one, and the store is emptied after an undo
/// and when another library is opened. Nothing is stored on disk.
/// </summary>
public sealed class BulkUndoStore : IDisposable
{
    private readonly object gate = new();
    private readonly ILibrarySession? session;
    private IReadOnlyDictionary<Id<MediaItem>, UserLayerSnapshot> snapshots = new Dictionary<Id<MediaItem>, UserLayerSnapshot>();

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkUndoStore"/> class.
    /// </summary>
    /// <param name="session">Holds the open library; its snapshots are dropped when it changes.</param>
    public BulkUndoStore(ILibrarySession? session = null)
    {
        this.session = session;
        if (session is not null)
        {
            session.CurrentChanged += OnLibraryChanged;
        }
    }

    /// <summary>
    /// Raised after the store got or lost its operation.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the number of items the stored operation changed, or 0 if there is nothing to undo.
    /// </summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return snapshots.Count;
            }
        }
    }

    /// <summary>
    /// Stores an operation in place of the earlier one.
    /// </summary>
    /// <param name="operation">What each changed item looked like before.</param>
    public void Set(IReadOnlyDictionary<Id<MediaItem>, UserLayerSnapshot> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (gate)
        {
            snapshots = operation;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes the stored operation out, so it can be undone once.
    /// </summary>
    /// <returns>The snapshots, empty if there was nothing to undo.</returns>
    public IReadOnlyDictionary<Id<MediaItem>, UserLayerSnapshot> Take()
    {
        IReadOnlyDictionary<Id<MediaItem>, UserLayerSnapshot> taken;
        lock (gate)
        {
            taken = snapshots;
            snapshots = new Dictionary<Id<MediaItem>, UserLayerSnapshot>();
        }

        if (taken.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return taken;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (session is not null)
        {
            session.CurrentChanged -= OnLibraryChanged;
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => Take();
}
