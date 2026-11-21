using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.Infrastructure.Libraries;

/// <summary>
/// Measures a library's disk usage by summing its files' sizes (E02-S05-T03), on a thread-pool thread so a large
/// thumbnail cache doesn't block the caller. One measurement runs per library at a time; callers asking meanwhile
/// share it, and its result is cached.
/// </summary>
/// <param name="clock">Dates the cached results.</param>
public sealed class LibraryDiskUsageMeter(TimeProvider clock) : ILibraryDiskUsage
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sums a library's file sizes now, without the cache.
    /// </summary>
    /// <param name="layout">Where the library's files are.</param>
    /// <returns>The disk usage.</returns>
    public static LibraryDiskUsage Measure(LibraryLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var database = FileSize(layout.DatabasePath) + FileSize(layout.DatabasePath + "-wal") + FileSize(layout.DatabasePath + "-shm");
        return new LibraryDiskUsage(database, FolderSize(layout.ThumbnailsDirectory), FolderSize(layout.BackupsDirectory));
    }

    /// <inheritdoc />
    public Task<LibraryDiskUsage> MeasureAsync(LibraryLayout layout, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Task<LibraryDiskUsage> measurement;
        var started = false;
        lock (gate)
        {
            entries.TryGetValue(layout.RootDirectory, out var entry);
            if (entry?.Running is { } running)
            {
                measurement = running;
            }
            else if (entry?.Result is { } result && clock.GetUtcNow() - entry.MeasuredAt <= maxAge)
            {
                return Task.FromResult(result);
            }
            else
            {
                measurement = Task.Run(() => Measure(layout), CancellationToken.None);
                entries[layout.RootDirectory] = new Entry(entry?.Result, entry?.MeasuredAt ?? default, measurement);
                started = true;
            }
        }

        // Registered outside the lock: a measurement that already finished runs the continuation at once.
        if (started)
        {
            _ = measurement.ContinueWith(
                task => Complete(layout.RootDirectory, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return measurement.WaitAsync(cancellationToken);
    }

    private static long FileSize(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long FolderSize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        // Files can be written or deleted while the walk runs, e.g. by a thumbnail job: those are skipped, not fatal.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                try
                {
                    total += file.Length;
                }
                catch (FileNotFoundException)
                {
                    // Deleted since it was listed.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // The folder was deleted during the walk, e.g. by "Regenerate all".
        }

        return total;
    }

    private void Complete(string root, Task<LibraryDiskUsage> measurement)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(root, out var entry) || !ReferenceEquals(entry.Running, measurement))
            {
                return;
            }

            entries[root] = measurement.IsCompletedSuccessfully
                ? new Entry(measurement.Result, clock.GetUtcNow(), null)
                : entry with { Running = null };
        }
    }

    private sealed record Entry(LibraryDiskUsage? Result, DateTimeOffset MeasuredAt, Task<LibraryDiskUsage>? Running);
}
