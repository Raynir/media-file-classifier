using System.ComponentModel;
using FFMpegCore.Exceptions;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MetadataExtractor;

namespace MediaFileClassifier.Infrastructure.Media;

/// <summary>
/// Classifies FFmpeg and decoder failures (E05-S04-T01). FFmpeg failing on a file gives the same result next time, so
/// it's permanent; FFmpeg that can't be started is a configuration problem.
/// </summary>
public sealed class MediaExceptionClassifier : IExceptionClassifier
{
    // Win32 ERROR_FILE_NOT_FOUND and ERROR_PATH_NOT_FOUND, when ffmpeg or ffprobe isn't where FFMpegCore looks.
    private const int FileNotFound = 2;
    private const int PathNotFound = 3;

    /// <inheritdoc />
    public ClassifiedError? TryClassify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            FFOptionsException => new(ErrorCategory.Configuration, exception.Message, exception),
            Win32Exception { NativeErrorCode: FileNotFound or PathNotFound } => new(ErrorCategory.Configuration, $"FFmpeg couldn't be started: {exception.Message}", exception),
            FFMpegException or FFProbeException or FFMpegArgumentException or ImageProcessingException => new(ErrorCategory.Permanent, exception.Message, exception),
            _ => null,
        };
    }
}
