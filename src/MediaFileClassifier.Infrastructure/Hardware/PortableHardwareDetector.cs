using System.Runtime.InteropServices;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.Hardware;

/// <summary>
/// Detects the processor and memory with portable .NET APIs. It lists no graphics adapters; platform heads
/// register a detector that does.
/// </summary>
public sealed class PortableHardwareDetector : IHardwareDetector
{
    /// <summary>
    /// Creates a profile of this machine's processor and memory, with the given adapters.
    /// </summary>
    /// <param name="gpus">Graphics adapters found by a platform-specific detector.</param>
    /// <returns>The profile.</returns>
    public static HardwareProfile CreateProfile(IReadOnlyList<GpuInfo> gpus)
        => new(
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            gpus);

    /// <inheritdoc />
    public HardwareProfile Detect() => CreateProfile([]);
}
