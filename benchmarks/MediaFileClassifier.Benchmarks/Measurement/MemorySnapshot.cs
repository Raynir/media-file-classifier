using System.Diagnostics;

namespace MediaFileClassifier.Benchmarks.Measurement;

/// <summary>
/// The process memory at one point. Private bytes include native memory such as SQLite's page
/// cache; <see cref="SqliteBytes"/> is SQLite's own heap counter.
/// </summary>
internal sealed record MemorySnapshot(long ManagedBytes, long PrivateBytes, long WorkingSetBytes, long PeakWorkingSetBytes, long SqliteBytes)
{
    public static MemorySnapshot Take()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            process.PrivateMemorySize64,
            process.WorkingSet64,
            process.PeakWorkingSet64,
            SQLitePCL.raw.sqlite3_memory_used());
    }
}
