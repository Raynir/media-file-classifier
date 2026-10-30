namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Every <see cref="CapabilityReason"/> code, with factories that supply the right arguments. A new code needs a
/// <c>CapabilityReason_&lt;Code&gt;</c> string in the UI resources; a test checks that each constant here has one.
/// </summary>
public static class CapabilityReasons
{
    /// <summary>
    /// The capability hasn't been checked yet.
    /// </summary>
    public const string NotChecked = "NotChecked";

    /// <summary>
    /// The platform has no file manager to show files in.
    /// </summary>
    public const string NoFileExplorer = "NoFileExplorer";

    /// <summary>
    /// FFmpeg isn't installed where the app looks for it.
    /// </summary>
    public const string FfmpegNotFound = "FfmpegNotFound";

    /// <summary>
    /// FFmpeg was found without FFprobe. Argument: the FFmpeg executable's path.
    /// </summary>
    public const string FfprobeNotFound = "FfprobeNotFound";

    /// <summary>
    /// A native dependency was found but failed to run. Arguments: its name and path.
    /// </summary>
    public const string DependencyFailedToRun = "DependencyFailedToRun";

    /// <summary>
    /// A native dependency ran but didn't report its version. Arguments: its name and path.
    /// </summary>
    public const string DependencyVersionUnknown = "DependencyVersionUnknown";

    /// <summary>
    /// Checking a native dependency failed. Argument: its name.
    /// </summary>
    public const string DependencyFailedToLoad = "DependencyFailedToLoad";

    /// <summary>
    /// Creates a <see cref="NotChecked"/> reason.
    /// </summary>
    /// <returns>The reason.</returns>
    public static CapabilityReason NotCheckedYet() => new(NotChecked, []);

    /// <summary>
    /// Creates a <see cref="NoFileExplorer"/> reason.
    /// </summary>
    /// <returns>The reason.</returns>
    public static CapabilityReason NoFileExplorerOnPlatform() => new(NoFileExplorer, []);

    /// <summary>
    /// Creates a <see cref="FfmpegNotFound"/> reason.
    /// </summary>
    /// <returns>The reason.</returns>
    public static CapabilityReason FfmpegMissing() => new(FfmpegNotFound, []);

    /// <summary>
    /// Creates a <see cref="FfprobeNotFound"/> reason.
    /// </summary>
    /// <param name="ffmpegPath">Path of the FFmpeg executable that was found.</param>
    /// <returns>The reason.</returns>
    public static CapabilityReason FfprobeMissing(string ffmpegPath) => new(FfprobeNotFound, [ffmpegPath]);

    /// <summary>
    /// Creates a <see cref="DependencyFailedToRun"/> reason.
    /// </summary>
    /// <param name="dependency">Dependency name, e.g. "FFmpeg".</param>
    /// <param name="path">Path of the executable or library.</param>
    /// <returns>The reason.</returns>
    public static CapabilityReason FailedToRun(string dependency, string path) => new(DependencyFailedToRun, [dependency, path]);

    /// <summary>
    /// Creates a <see cref="DependencyVersionUnknown"/> reason.
    /// </summary>
    /// <param name="dependency">Dependency name, e.g. "FFmpeg".</param>
    /// <param name="path">Path of the executable or library.</param>
    /// <returns>The reason.</returns>
    public static CapabilityReason VersionUnknown(string dependency, string path) => new(DependencyVersionUnknown, [dependency, path]);

    /// <summary>
    /// Creates a <see cref="DependencyFailedToLoad"/> reason.
    /// </summary>
    /// <param name="dependency">Dependency name, e.g. "Whisper".</param>
    /// <returns>The reason.</returns>
    public static CapabilityReason FailedToLoad(string dependency) => new(DependencyFailedToLoad, [dependency]);
}
