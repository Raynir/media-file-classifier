using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediatR;

namespace MediaFileClassifier.UseCases.Hardware.GetHardwareStatus;

/// <summary>
/// Gets what the hardware settings show (E17-S08-T02): the detected execution provider, which providers can be used
/// here and why not, the installed tiers of the embedding model, and the latest benchmark.
/// </summary>
public sealed record GetHardwareStatusQuery : IRequest<HardwareStatus>;

/// <summary>
/// What the hardware settings show.
/// </summary>
/// <param name="Detected">The execution provider used when the user hasn't chosen one.</param>
/// <param name="GpuName">The graphics adapter GPU providers run on, or <see langword="null"/> when none qualifies.</param>
/// <param name="Providers">The platform's execution providers, each with whether it can be used.</param>
/// <param name="InstalledTiers">The installed tiers of the embedding model, by tier ID.</param>
/// <param name="Benchmark">The latest benchmark of this machine, or <see langword="null"/> when it never ran.</param>
public sealed record HardwareStatus(
    ExecutionProvider Detected,
    string? GpuName,
    IReadOnlyList<ExecutionProviderAvailability> Providers,
    IReadOnlyList<string> InstalledTiers,
    BenchmarkReport? Benchmark);
