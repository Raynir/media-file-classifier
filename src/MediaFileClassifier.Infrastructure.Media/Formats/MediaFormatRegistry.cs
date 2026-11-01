using MediaFileClassifier.Infrastructure.Abstractions.Formats;

namespace MediaFileClassifier.Infrastructure.Media.Formats;

/// <summary>
/// Detects formats with the registered <see cref="IMediaFormatHandler"/> services: the extension selects the files to
/// read, the leading bytes pick the handler, and the handler tells the kind of media.
/// </summary>
public sealed class MediaFormatRegistry : IMediaFormatRegistry
{
    private readonly Dictionary<string, IMediaFormatHandler> byFormatId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IMediaFormatHandler> byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly int headerLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaFormatRegistry"/> class.
    /// </summary>
    /// <param name="handlers">The format handlers.</param>
    /// <exception cref="InvalidOperationException">Two handlers share a format id or an extension.</exception>
    public MediaFormatRegistry(IEnumerable<IMediaFormatHandler> handlers)
    {
        Handlers = [.. handlers];
        foreach (var handler in Handlers)
        {
            if (!byFormatId.TryAdd(handler.FormatId, handler))
            {
                throw new InvalidOperationException($"The format '{handler.FormatId}' is registered twice.");
            }

            foreach (var extension in handler.Extensions)
            {
                if (!byExtension.TryAdd(extension, handler))
                {
                    throw new InvalidOperationException(
                        $"The extension '{extension}' is registered by both '{byExtension[extension].FormatId}' and '{handler.FormatId}'.");
                }
            }
        }

        headerLength = Handlers.Count == 0 ? 0 : Handlers.Max(handler => handler.SignatureLength);
    }

    /// <inheritdoc />
    public IReadOnlyList<IMediaFormatHandler> Handlers { get; }

    /// <inheritdoc />
    public bool IsCandidate(string fileName) => byExtension.ContainsKey(Path.GetExtension(fileName));

    /// <inheritdoc />
    public IMediaFormatHandler? FindByFormatId(string formatId) => byFormatId.GetValueOrDefault(formatId);

    /// <inheritdoc />
    public async Task<MediaFormatDetection> DetectAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName);
        if (!byExtension.TryGetValue(extension, out var expected))
        {
            return MediaFormatDetection.Rejected(new(MediaFormatIssues.UnsupportedExtension, extension.ToLowerInvariant()));
        }

        IMediaFormatHandler? handler = null;
        try
        {
            var header = new byte[headerLength];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            if (read == 0)
            {
                return MediaFormatDetection.Rejected(new(MediaFormatIssues.EmptyFile));
            }

            handler = FindBySignature(header.AsSpan(0, read), expected);
            if (handler is null)
            {
                return MediaFormatDetection.Rejected(new(MediaFormatIssues.UnrecognizedContent));
            }

            var inspection = await handler.InspectAsync(Rewind(content, header, read), cancellationToken);
            if (inspection.MediaType is not { } mediaType)
            {
                return MediaFormatDetection.Rejected(inspection.Rejection ?? new(MediaFormatIssues.UnrecognizedContent), handler);
            }

            IReadOnlyList<MediaFormatIssue> warnings = handler == expected ? [] : [new(MediaFormatIssues.ExtensionMismatch, handler.FormatId)];
            return MediaFormatDetection.Supported(handler, mediaType, warnings);
        }
        catch (IOException ex)
        {
            return MediaFormatDetection.Rejected(new(MediaFormatIssues.Unreadable, ex.Message), handler);
        }
    }

    /// <summary>
    /// Gives the handler the content from the start again: seeks back, or replays the header in front of a stream that
    /// can't seek, such as an Android content stream.
    /// </summary>
    private static Stream Rewind(Stream content, byte[] header, int read)
    {
        if (content.CanSeek)
        {
            content.Seek(-read, SeekOrigin.Current);
            return content;
        }

        return new PrefixedReadStream(header.AsMemory(0, read), content);
    }

    /// <summary>
    /// Tries the handler the extension names first, so the most likely match wins, then every other handler.
    /// </summary>
    private IMediaFormatHandler? FindBySignature(ReadOnlySpan<byte> header, IMediaFormatHandler expected)
    {
        if (expected.MatchesSignature(header))
        {
            return expected;
        }

        foreach (var handler in Handlers)
        {
            if (handler != expected && handler.MatchesSignature(header))
            {
                return handler;
            }
        }

        return null;
    }
}
