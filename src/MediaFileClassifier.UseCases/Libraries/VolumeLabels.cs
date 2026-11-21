using MediaFileClassifier.Infrastructure.Abstractions.Files;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// Reads a folder's volume label (E14-S07-T02) and tells a label that couldn't be read apart from a volume without one,
/// so a check keeps the label it already stored rather than clearing it.
/// </summary>
internal static class VolumeLabels
{
    /// <summary>
    /// Gets the result for a label that wasn't read.
    /// </summary>
    public static (bool Read, string? Label) Unknown { get; } = (false, null);

    /// <summary>
    /// Reads the label of the volume a folder is on.
    /// </summary>
    /// <param name="files">Reads the label.</param>
    /// <param name="folder">The folder, which should be reachable.</param>
    /// <param name="logger">Logs a label that couldn't be read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the label was read, and the label, <see langword="null"/> for none.</returns>
    public static async Task<(bool Read, string? Label)> TryReadAsync(IMediaFileSource files, FolderHandle folder, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return (true, await files.GetVolumeLabelAsync(folder, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't read the volume label of {Folder}", folder.DisplayName);
            return Unknown;
        }
    }
}
