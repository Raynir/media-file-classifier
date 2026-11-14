using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.Hardware;

/// <summary>
/// Detects the hardware once, and chooses the execution provider and parallelism from it.
/// </summary>
/// <param name="detector">The platform's hardware detector.</param>
public sealed class HardwareInfo(IHardwareDetector detector) : IHardwareInfo
{
    /// <summary>
    /// Dedicated video memory an adapter needs before DirectML is preferred over the CPU.
    /// </summary>
    public const long MinimumGpuMemoryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Upper limit of <see cref="DefaultParallelism"/>.
    /// </summary>
    public const int MaximumParallelism = 8;

    private readonly Lazy<HardwareProfile> profile = new(detector.Detect);

    /// <inheritdoc />
    public HardwareProfile Profile => profile.Value;

    /// <inheritdoc />
    public ExecutionProvider PreferredExecutionProvider => ChooseExecutionProvider(Profile);

    /// <inheritdoc />
    public GpuInfo? PreferredGpu => ChooseGpu(Profile);

    /// <inheritdoc />
    public int DefaultParallelism => ChooseParallelism(Profile);

    /// <summary>
    /// Chooses DirectML when an adapter has at least <see cref="MinimumGpuMemoryBytes"/> of dedicated memory, and the CPU otherwise.
    /// </summary>
    /// <param name="hardware">Detected hardware.</param>
    /// <returns>The execution provider to try first.</returns>
    public static ExecutionProvider ChooseExecutionProvider(HardwareProfile hardware)
        => ChooseGpu(hardware) is null ? ExecutionProvider.Cpu : ExecutionProvider.DirectMl;

    /// <summary>
    /// Chooses the adapter with the most dedicated memory, if it has at least <see cref="MinimumGpuMemoryBytes"/>. On a
    /// tie, the first one listed wins.
    /// </summary>
    /// <param name="hardware">Detected hardware.</param>
    /// <returns>The adapter, or <c>null</c> when none qualifies.</returns>
    public static GpuInfo? ChooseGpu(HardwareProfile hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        GpuInfo? best = null;
        foreach (var gpu in hardware.Gpus)
        {
            if (gpu.DedicatedMemoryBytes >= MinimumGpuMemoryBytes && gpu.DedicatedMemoryBytes > (best?.DedicatedMemoryBytes ?? 0))
            {
                best = gpu;
            }
        }

        return best;
    }

    /// <summary>
    /// Uses half the logical processors, from 1 to <see cref="MaximumParallelism"/>, so the machine stays responsive.
    /// </summary>
    /// <param name="hardware">Detected hardware.</param>
    /// <returns>The default number of files processed in parallel.</returns>
    public static int ChooseParallelism(HardwareProfile hardware)
        => Math.Clamp(hardware.LogicalProcessorCount / 2, 1, MaximumParallelism);
}
