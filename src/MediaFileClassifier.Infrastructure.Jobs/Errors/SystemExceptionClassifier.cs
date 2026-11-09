using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;

namespace MediaFileClassifier.Infrastructure.Jobs.Errors;

/// <summary>
/// Classifies the .NET exceptions every module can throw (E05-S04-T01): file I/O, HTTP, timeouts and decoders.
/// </summary>
public sealed class SystemExceptionClassifier : IExceptionClassifier
{
    // Windows error codes, as IOException.HResult (0x8007xxxx).
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int DiskFull = unchecked((int)0x80070070);

    /// <inheritdoc />
    public ClassifiedError? TryClassify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Files: a locked file or an offline volume passes; a missing, damaged or forbidden file doesn't.
            IOException { HResult: SharingViolation or LockViolation } => Transient(exception, $"The file is in use by another program: {exception.Message}"),
            IOException { HResult: HandleDiskFull or DiskFull } => Classify(ErrorCategory.Configuration, exception),
            FileNotFoundException or PathTooLongException or EndOfStreamException => Classify(ErrorCategory.Permanent, exception),
            DirectoryNotFoundException or DriveNotFoundException => Transient(exception),
            UnauthorizedAccessException => Classify(ErrorCategory.Permanent, exception),

            // HTTP: no status means the server wasn't reached.
            HttpRequestException { StatusCode: null } => Transient(exception),
            HttpRequestException { StatusCode: { } status } => IsTransient(status)
                ? Transient(exception)
                : Classify(ErrorCategory.Permanent, exception),
            SocketException or TimeoutException => Transient(exception),

            // Not cancelled by the job (that's handled before classifying): a timeout.
            OperationCanceledException => Transient(exception, "The operation timed out."),

            // Other I/O errors, e.g. a network share that dropped for a moment.
            IOException => Transient(exception),

            // Decoders and parsers: the content is damaged or not supported.
            InvalidDataException or FormatException or NotSupportedException or JsonException => Classify(ErrorCategory.Permanent, exception),

            // Native libraries that aren't installed.
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException => Classify(ErrorCategory.Configuration, exception),
            _ => null,
        };
    }

    /// <summary>
    /// Gets whether an HTTP status is worth retrying: rate limits, timeouts and server errors.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns><see langword="true"/> if retrying can help.</returns>
    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500;

    private static ClassifiedError Transient(Exception exception, string? message = null) =>
        new(ErrorCategory.Transient, message ?? exception.Message, exception);

    private static ClassifiedError Classify(ErrorCategory category, Exception exception) =>
        new(category, exception.Message, exception);
}
