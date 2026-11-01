using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

namespace MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;

/// <summary>
/// What a probe found out about a native dependency.
/// </summary>
/// <param name="IsAvailable">Whether the dependency loads and works.</param>
/// <param name="Version">Reported version, if known.</param>
/// <param name="Location">Where it was found, if it is a file.</param>
/// <param name="Problem">Why it is unavailable, shown to the user through the capabilities it gates; <c>null</c> when available.</param>
/// <param name="Detail">Technical detail for the log only, such as the build flavour or an error message.</param>
public sealed record NativeDependencyStatus(bool IsAvailable, string? Version, string? Location, CapabilityReason? Problem, string? Detail)
{
    /// <summary>
    /// Creates the status of a working dependency.
    /// </summary>
    /// <param name="version">Reported version.</param>
    /// <param name="location">Where it was found.</param>
    /// <param name="detail">Technical detail for the log, e.g. "LGPL build".</param>
    /// <returns>The status.</returns>
    public static NativeDependencyStatus Available(string? version, string? location, string? detail = null)
        => new(true, version, location, null, detail);

    /// <summary>
    /// Creates the status of a missing or broken dependency.
    /// </summary>
    /// <param name="problem">Why it is unavailable, e.g. <see cref="CapabilityReasons.FfmpegMissing"/>.</param>
    /// <param name="location">Where the broken file was found, if any.</param>
    /// <param name="detail">Technical detail for the log, such as an exception message.</param>
    /// <returns>The status.</returns>
    public static NativeDependencyStatus Unavailable(CapabilityReason problem, string? location = null, string? detail = null)
        => new(false, null, location, problem, detail);
}
