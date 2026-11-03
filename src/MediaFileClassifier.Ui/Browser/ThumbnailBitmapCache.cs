using Avalonia.Media.Imaging;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Decoded thumbnails of the media browser (E14-S01-T03): an LRU cache bounded by the bitmaps' memory. Files are
/// decoded on the thread pool, a few at a time, and each caller gets a <see cref="ThumbnailLease"/>. A bitmap that
/// falls out of the cache while a tile still shows it is disposed only when the tile lets go of it.
/// </summary>
public sealed class ThumbnailBitmapCache : IDisposable
{
    /// <summary>
    /// The default memory budget: about 600 small or 160 large thumbnails.
    /// </summary>
    public const long DefaultBudgetBytes = 128L * 1024 * 1024;

    private const int DefaultConcurrentDecodes = 4;

    private readonly long budgetBytes;
    private readonly Func<string, Bitmap?> decode;
    private readonly SemaphoreSlim decodes;
    private readonly Lock syncRoot = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<Entry?>> loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> recency = [];
    private long usedBytes;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailBitmapCache"/> class with the default budget.
    /// </summary>
    public ThumbnailBitmapCache()
        : this(DefaultBudgetBytes, DefaultConcurrentDecodes, DecodeFile)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailBitmapCache"/> class.
    /// </summary>
    /// <param name="budgetBytes">The most bitmap memory kept for reuse, in bytes.</param>
    /// <param name="concurrentDecodes">The most files decoded at once.</param>
    /// <param name="decode">Decodes a file, or returns <see langword="null"/> when it can't.</param>
    public ThumbnailBitmapCache(long budgetBytes, int concurrentDecodes, Func<string, Bitmap?> decode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrentDecodes);

        this.budgetBytes = budgetBytes;
        this.decode = decode;
        decodes = new SemaphoreSlim(concurrentDecodes);
    }

    /// <summary>
    /// Gets the memory of the bitmaps the cache keeps, in bytes.
    /// </summary>
    public long UsedBytes
    {
        get
        {
            lock (syncRoot)
            {
                return usedBytes;
            }
        }
    }

    /// <summary>
    /// Gets a thumbnail file's bitmap, from the cache or by decoding it.
    /// </summary>
    /// <param name="path">The thumbnail file.</param>
    /// <param name="version">Changes when the file is rewritten, e.g. its write time, so a stale bitmap isn't reused.</param>
    /// <param name="cancellationToken">Cancels waiting, e.g. when the tile scrolled away. A decode already running finishes for the cache.</param>
    /// <returns>A lease on the bitmap, which the caller disposes, or <see langword="null"/> when the file can't be decoded.</returns>
    public async Task<ThumbnailLease?> LoadAsync(string path, long version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var key = string.Concat(path, "|", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Task<Entry?> task;
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (entries.TryGetValue(key, out var cached))
            {
                return Lease(cached);
            }

            if (!loading.TryGetValue(key, out task!))
            {
                task = DecodeAsync(key, path);
                loading[key] = task;
            }
        }

        var entry = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        lock (syncRoot)
        {
            // Evicted between decoding and now: the bitmap is still good while someone holds it.
            return entry.Bitmap is null ? null : Lease(entry);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (syncRoot)
        {
            disposed = true;
            foreach (var entry in recency.ToList())
            {
                Evict(entry);
            }
        }

        // A decode still running then fails to release it; DecodeAsync treats that as a failed decode.
        decodes.Dispose();
    }

    private static Bitmap? DecodeFile(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Deleted or rewritten meanwhile, or not decodable: the tile shows its placeholder.
            return null;
        }
    }

    private async Task<Entry?> DecodeAsync(string key, string path)
    {
        Bitmap? bitmap = null;
        try
        {
            await decodes.WaitAsync().ConfigureAwait(false);
            try
            {
                bitmap = await Task.Run(() => decode(path)).ConfigureAwait(false);
            }
            finally
            {
                decodes.Release();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any decode failure means a placeholder, never a load stuck in the map.
        }

        lock (syncRoot)
        {
            loading.Remove(key);
            if (bitmap is null)
            {
                return null;
            }

            if (disposed)
            {
                bitmap.Dispose();
                return null;
            }

            var entry = new Entry(key, bitmap, (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
            entry.Node = recency.AddFirst(entry);
            entries[key] = entry;
            usedBytes += entry.Bytes;
            Trim(keep: entry);
            return entry;
        }
    }

    private ThumbnailLease Lease(Entry entry)
    {
        entry.Leases++;
        if (entry.Node is not null)
        {
            recency.Remove(entry.Node);
            recency.AddFirst(entry.Node);
        }

        return new ThumbnailLease(entry.Bitmap!, () => Release(entry));
    }

    private void Release(Entry entry)
    {
        lock (syncRoot)
        {
            entry.Leases--;
            if (entry.Leases == 0 && entry.Node is null)
            {
                entry.Bitmap?.Dispose();
                entry.Bitmap = null;
            }
        }
    }

    private void Trim(Entry keep)
    {
        while (usedBytes > budgetBytes && recency.Last is { } last && last.Value != keep)
        {
            Evict(last.Value);
        }
    }

    private void Evict(Entry entry)
    {
        recency.Remove(entry.Node!);
        entry.Node = null;
        entries.Remove(entry.Key);
        usedBytes -= entry.Bytes;
        if (entry.Leases == 0)
        {
            entry.Bitmap?.Dispose();
            entry.Bitmap = null;
        }
    }

    private sealed class Entry(string key, Bitmap bitmap, long bytes)
    {
        public string Key { get; } = key;

        public long Bytes { get; } = bytes;

        public Bitmap? Bitmap { get; set; } = bitmap;

        public LinkedListNode<Entry>? Node { get; set; }

        public int Leases { get; set; }
    }
}

/// <summary>
/// A tile's hold on a cached thumbnail. Dispose it when the tile shows something else.
/// </summary>
public sealed class ThumbnailLease : IDisposable
{
    private Action? release;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailLease"/> class.
    /// </summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="release">Lets go of the bitmap.</param>
    internal ThumbnailLease(Bitmap bitmap, Action release)
    {
        Bitmap = bitmap;
        this.release = release;
    }

    /// <summary>
    /// Gets the bitmap. Don't use it after disposing the lease.
    /// </summary>
    public Bitmap Bitmap { get; }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
