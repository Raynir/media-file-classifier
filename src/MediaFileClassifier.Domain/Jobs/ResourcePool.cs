namespace MediaFileClassifier.Domain.Jobs;

/// <summary>
/// The resource a work item mostly uses. Each pool has its own concurrency limit (E05-S01).
/// </summary>
public enum ResourcePool
{
    /// <summary>
    /// Disk and network reads, e.g. discovery and hashing.
    /// </summary>
    Io,

    /// <summary>
    /// Processor work, e.g. metadata extraction and thumbnails.
    /// </summary>
    Cpu,

    /// <summary>
    /// AI model inference, local or remote.
    /// </summary>
    Ai,
}
