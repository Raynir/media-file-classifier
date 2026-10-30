namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// The machine the app runs on.
/// </summary>
/// <param name="Architecture">Process architecture, e.g. "X64".</param>
/// <param name="LogicalProcessorCount">Number of logical processors available to the process.</param>
/// <param name="TotalMemoryBytes">Physical memory available to the process, in bytes.</param>
/// <param name="Gpus">Hardware graphics adapters. Empty when none were found or the platform can't list them.</param>
public sealed record HardwareProfile(string Architecture, int LogicalProcessorCount, long TotalMemoryBytes, IReadOnlyList<GpuInfo> Gpus);
