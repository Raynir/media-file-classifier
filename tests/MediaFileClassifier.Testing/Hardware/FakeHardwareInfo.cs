using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Testing.Hardware;

/// <summary>
/// A fixed machine: the preferred execution provider, graphics adapter, core count, memory and parallelism are
/// the constructor's arguments. Defaults to an 8-core, 16 GB machine with no GPU that prefers the CPU.
/// </summary>
/// <param name="preferredExecutionProvider">The provider <see cref="PreferredExecutionProvider"/> returns.</param>
/// <param name="gpu">The graphics adapter, or <see langword="null"/> for none.</param>
/// <param name="defaultParallelism">The value of <see cref="DefaultParallelism"/>.</param>
/// <param name="cpuCores">The logical cores in <see cref="Profile"/>.</param>
/// <param name="memoryBytes">The memory in <see cref="Profile"/>.</param>
public sealed class FakeHardwareInfo(
    ExecutionProvider preferredExecutionProvider = ExecutionProvider.Cpu,
    GpuInfo? gpu = null,
    int defaultParallelism = 4,
    int cpuCores = 8,
    long memoryBytes = 16L << 30) : IHardwareInfo
{
    /// <inheritdoc />
    public HardwareProfile Profile { get; } = new("X64", cpuCores, memoryBytes, gpu is null ? [] : [gpu]);

    /// <inheritdoc />
    public ExecutionProvider PreferredExecutionProvider => preferredExecutionProvider;

    /// <inheritdoc />
    public GpuInfo? PreferredGpu => gpu;

    /// <inheritdoc />
    public int DefaultParallelism => defaultParallelism;
}
