using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// The deleted paths of watched folders waiting out the grace period (E04-S02-T02), in memory. <see cref="ChangeProcessor"/>
/// holds and releases them; <see cref="ApplyChangesStage"/> pairs their items with new files. It depends on nothing, so
/// the stage can use it without depending on the job system that runs the stage.
/// </summary>
public sealed class HeldDeletes
{
    private readonly Lock syncRoot = new();

    // Paths by folder, with when they're due. They belong to the owner (the library's scheduler) they were held for.
    private readonly Dictionary<Id<IndexedFolder>, Dictionary<string, DateTimeOffset>> held = [];
    private object? owner;

    /// <summary>
    /// Gets the owner the paths are held for.
    /// </summary>
    public object? Owner
    {
        get
        {
            lock (syncRoot)
            {
                return owner;
            }
        }
    }

    /// <summary>
    /// Gets the paths held now, by folder.
    /// </summary>
    /// <returns>The folders and paths.</returns>
    public IReadOnlyList<(Id<IndexedFolder> FolderId, string Path)> GetAll()
    {
        lock (syncRoot)
        {
            return [.. held.SelectMany(folder => folder.Value.Keys.Select(path => (folder.Key, path)))];
        }
    }

    /// <summary>
    /// Holds paths until a time. Paths held for another owner are dropped: they mean nothing in another library.
    /// </summary>
    /// <param name="holder">The owner, e.g. the open library's scheduler.</param>
    /// <param name="folderId">The folder.</param>
    /// <param name="paths">The deleted paths.</param>
    /// <param name="due">When they're released.</param>
    public void Hold(object holder, Id<IndexedFolder> folderId, IEnumerable<string> paths, DateTimeOffset due)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(paths);

        lock (syncRoot)
        {
            if (!ReferenceEquals(owner, holder))
            {
                held.Clear();
                owner = holder;
            }

            if (!held.TryGetValue(folderId, out var folderPaths))
            {
                folderPaths = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                held[folderId] = folderPaths;
            }

            foreach (var path in paths)
            {
                folderPaths[path] = due;
            }
        }
    }

    /// <summary>
    /// Takes the paths that are due.
    /// </summary>
    /// <param name="now">The time now.</param>
    /// <returns>The owner they were held for, and the due paths by folder.</returns>
    public (object? Owner, IReadOnlyList<(Id<IndexedFolder> FolderId, IReadOnlyList<string> Paths)> Due) TakeDue(DateTimeOffset now)
    {
        lock (syncRoot)
        {
            var due = new List<(Id<IndexedFolder>, IReadOnlyList<string>)>();
            foreach (var (folderId, paths) in held)
            {
                var ready = paths.Where(path => path.Value <= now).Select(path => path.Key).ToList();
                foreach (var path in ready)
                {
                    paths.Remove(path);
                }

                if (ready.Count > 0)
                {
                    due.Add((folderId, ready));
                }
            }

            foreach (var folderId in held.Where(folder => folder.Value.Count == 0).Select(folder => folder.Key).ToList())
            {
                held.Remove(folderId);
            }

            return (owner, due);
        }
    }

    /// <summary>
    /// Gets when the next path is due.
    /// </summary>
    /// <returns>The earliest due time, or <see langword="null"/> when nothing is held.</returns>
    public DateTimeOffset? NextDue()
    {
        lock (syncRoot)
        {
            return held.Values.SelectMany(paths => paths.Values).Select(due => (DateTimeOffset?)due).Min();
        }
    }
}
