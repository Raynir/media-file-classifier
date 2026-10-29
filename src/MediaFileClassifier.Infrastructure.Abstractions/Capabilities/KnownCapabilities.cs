namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Capabilities of the platform and the native dependencies (E01-S07, E01-S08). Other Stories define their own
/// <see cref="CapabilityDefinition"/> next to the <see cref="ICapabilityContributor"/> that sets it.
/// </summary>
public static class KnownCapabilities
{
    /// <summary>
    /// Showing a file in the platform's file manager.
    /// </summary>
    public static readonly CapabilityDefinition RevealInFileExplorer = new("platform.reveal-in-file-explorer", "Show in file explorer");

    /// <summary>
    /// Reading video metadata and frames, and extracting audio for transcription. Needs FFmpeg.
    /// </summary>
    public static readonly CapabilityDefinition VideoAnalysis = new("media.video-analysis", "Video analysis");
}
