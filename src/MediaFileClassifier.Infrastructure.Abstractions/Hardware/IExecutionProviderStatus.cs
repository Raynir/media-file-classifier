namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// Which execution providers this machine and build can use (E17-S08-T02, E01-S07-T03).
/// </summary>
public interface IExecutionProviderStatus
{
    /// <summary>
    /// Gets the execution provider the app uses when the user hasn't chosen one: the hardware's preference if the
    /// runtime has it, else the CPU.
    /// </summary>
    ExecutionProvider Detected { get; }

    /// <summary>
    /// Gets the graphics adapter GPU providers run on, or <see langword="null"/> when none qualifies.
    /// </summary>
    GpuInfo? Gpu { get; }

    /// <summary>
    /// Lists the execution providers of this platform, the CPU first, each with whether it can be used here.
    /// </summary>
    /// <returns>The providers.</returns>
    IReadOnlyList<ExecutionProviderAvailability> GetProviders();
}

/// <summary>
/// Why an execution provider can't be used.
/// </summary>
public enum ExecutionProviderUnavailableReason
{
    /// <summary>The app's ONNX Runtime build doesn't include it, e.g. CUDA in the DirectML build.</summary>
    NotInRuntime,

    /// <summary>No graphics adapter qualifies, e.g. none has 2 GB of dedicated memory.</summary>
    NoSuitableGpu,
}

/// <summary>
/// One execution provider and whether it can be used.
/// </summary>
/// <param name="Provider">The provider.</param>
/// <param name="UnavailableReason">Why it can't be used, or <see langword="null"/> when it can.</param>
public sealed record ExecutionProviderAvailability(ExecutionProvider Provider, ExecutionProviderUnavailableReason? UnavailableReason)
{
    /// <summary>
    /// Gets a value indicating whether the provider can be used.
    /// </summary>
    public bool IsAvailable => UnavailableReason is null;
}
