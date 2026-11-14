namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Result of a provider health check.
/// </summary>
public enum ProviderHealthStatus
{
    /// <summary>
    /// The provider is ready to use.
    /// </summary>
    Healthy,

    /// <summary>
    /// The model isn't installed. Jobs that need it wait instead of failing (D037).
    /// </summary>
    ModelMissing,

    /// <summary>
    /// The endpoint didn't answer, or doesn't serve the configured model.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The native library or runtime the provider needs didn't load.
    /// </summary>
    NativeLibraryMissing,

    /// <summary>
    /// The endpoint isn't on this machine and the user hasn't confirmed that images may be sent to it.
    /// </summary>
    RemoteHostNotConfirmed,

    /// <summary>
    /// The provider doesn't run on this platform.
    /// </summary>
    NotSupportedOnPlatform,

    /// <summary>
    /// Any other problem, e.g. a model file that ONNX Runtime can't load.
    /// </summary>
    Unhealthy,
}
