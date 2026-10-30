namespace MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;

/// <summary>
/// What a probe found out about a native dependency.
/// </summary>
/// <param name="IsAvailable">Whether the dependency loads and works.</param>
/// <param name="Version">Reported version, if known.</param>
/// <param name="Location">Where it was found, if it is a file.</param>
/// <param name="Detail">One sentence for the user: what was found, or why it is unavailable.</param>
public sealed record NativeDependencyStatus(bool IsAvailable, string? Version, string? Location, string Detail)
{
    /// <summary>
    /// Creates the status of a working dependency.
    /// </summary>
    /// <param name="version">Reported version.</param>
    /// <param name="location">Where it was found.</param>
    /// <param name="detail">What was found, e.g. "FFmpeg 7.1 found.".</param>
    /// <returns>The status.</returns>
    public static NativeDependencyStatus Available(string? version, string? location, string detail)
        => new(true, version, location, detail);

    /// <summary>
    /// Creates the status of a missing or broken dependency.
    /// </summary>
    /// <param name="cause">Why it is unavailable, and what the user can do, e.g. "FFmpeg not found. Install it.".</param>
    /// <param name="location">Where the broken file was found, if any.</param>
    /// <returns>The status.</returns>
    public static NativeDependencyStatus Unavailable(string cause, string? location = null)
        => new(false, null, location, cause);
}
