using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.AI.Onnx;

/// <summary>
/// Tells which execution providers this machine and the app's ONNX Runtime build can use (E17-S08-T02): a GPU provider
/// needs to be in the runtime and a graphics adapter that qualifies (at least 2 GB of dedicated memory, as the hardware detection decides).
/// </summary>
/// <param name="sessionFactory">The session factory, which knows the runtime's providers.</param>
/// <param name="hardware">The detected hardware, or <see langword="null"/> when it isn't known (then GPUs don't qualify).</param>
public sealed class ExecutionProviderStatus(OnnxSessionFactory sessionFactory, IHardwareInfo? hardware = null) : IExecutionProviderStatus
{
    private readonly Lazy<IReadOnlyList<ExecutionProvider>> runtime = new(() => ReadRuntime(sessionFactory));

    /// <inheritdoc />
    public ExecutionProvider Detected =>
        OnnxSessionFactory.GetAttemptOrder(sessionFactory.DefaultExecutionProvider, [.. runtime.Value])[0];

    /// <inheritdoc />
    public GpuInfo? Gpu => hardware?.PreferredGpu;

    /// <summary>
    /// Gets a value indicating whether the app runs on Android, which has NNAPI instead of the desktop GPU providers.
    /// Tests set it.
    /// </summary>
    internal bool IsAndroid { get; init; } = OperatingSystem.IsAndroid();

    /// <inheritdoc />
    public IReadOnlyList<ExecutionProviderAvailability> GetProviders()
    {
        ExecutionProvider[] platform = IsAndroid
            ? [ExecutionProvider.Cpu, ExecutionProvider.Nnapi]
            : [ExecutionProvider.Cpu, ExecutionProvider.DirectMl, ExecutionProvider.Cuda];
        return [.. platform.Select(provider => new ExecutionProviderAvailability(provider, ReasonUnavailable(provider)))];
    }

    private static IReadOnlyList<ExecutionProvider> ReadRuntime(OnnxSessionFactory factory)
    {
        try
        {
            return factory.AvailableProviders();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            // Without the native runtime nothing runs; the CPU is still what the app would ask for.
            return [ExecutionProvider.Cpu];
        }
    }

    private ExecutionProviderUnavailableReason? ReasonUnavailable(ExecutionProvider provider)
    {
        if (provider == ExecutionProvider.Cpu)
        {
            return null;
        }

        if (!runtime.Value.Contains(provider))
        {
            return ExecutionProviderUnavailableReason.NotInRuntime;
        }

        return provider switch
        {
            ExecutionProvider.Nnapi => null,
            ExecutionProvider.Cuda when Gpu is not { Vendor: var vendor } || !vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                => ExecutionProviderUnavailableReason.NoSuitableGpu,
            _ when Gpu is null => ExecutionProviderUnavailableReason.NoSuitableGpu,
            _ => null,
        };
    }
}
