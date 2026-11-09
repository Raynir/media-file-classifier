namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Reason codes of <see cref="AnalyzerState"/>, for a skipped or failed analyzer. They're stored, so they never change.
/// A failure from a frame sampling warning uses the warning's code instead.
/// </summary>
public static class AnalyzerReasons
{
    /// <summary>
    /// The analyzer is disabled in the library's analysis profile.
    /// </summary>
    public const string Disabled = "Disabled";

    /// <summary>
    /// No provider has the analyzer's capability, or the profile names one that isn't registered.
    /// </summary>
    public const string NoProvider = "NoProvider";

    /// <summary>
    /// Providers exist, but none runs on this platform.
    /// </summary>
    public const string UnsupportedPlatform = "UnsupportedPlatform";

    /// <summary>
    /// The provider's model isn't installed (D037). Retryable once it is.
    /// </summary>
    public const string ModelMissing = "ModelMissing";

    /// <summary>
    /// The provider is paused by its circuit breaker (E05-S04), so it wasn't called. Retryable.
    /// </summary>
    public const string ProviderUnavailable = "ProviderUnavailable";

    /// <summary>
    /// The provider failed; <see cref="AnalyzerState.FailureKind"/> says how.
    /// </summary>
    public const string ProviderFailed = "ProviderFailed";

    /// <summary>
    /// Something other than the provider failed, e.g. the file couldn't be read or decoded.
    /// </summary>
    public const string InputFailed = "InputFailed";

    /// <summary>
    /// The video or animated image has no keyframes, because the embedding (which needs them) couldn't get any.
    /// </summary>
    public const string NoFrames = "NoFrames";
}
