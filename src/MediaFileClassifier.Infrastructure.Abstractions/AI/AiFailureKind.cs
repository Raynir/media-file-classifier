namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Why a provider call failed, so the pipeline and the circuit breaker (E05-S04) can tell what to do.
/// </summary>
public enum AiFailureKind
{
    /// <summary>
    /// Any other provider failure, such as a native library that won't load or an HTTP error.
    /// </summary>
    ProviderError,

    /// <summary>
    /// The model isn't installed. Not an error: the job waits for the model (D037).
    /// </summary>
    ModelMissing,

    /// <summary>
    /// The endpoint didn't answer or the connection dropped. Retrying later may help.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The call took too long.
    /// </summary>
    Timeout,

    /// <summary>
    /// The model answered, but the output didn't match the contract even after repair and retry (E01-S05-T05).
    /// The item's run fails; the provider itself is fine.
    /// </summary>
    InvalidOutput,

    /// <summary>
    /// The input can't be processed, e.g. an image the model can't decode. Retrying won't help.
    /// </summary>
    InvalidInput,

    /// <summary>
    /// The endpoint isn't local and the user hasn't confirmed sending images to it.
    /// </summary>
    RemoteHostNotConfirmed,
}
