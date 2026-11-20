using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Stores keyframe pictures as WebP files (E14-S13-T01, D227). See <see cref="IKeyframeThumbnails"/>.
/// </summary>
/// <param name="logger">Logs files that couldn't be written.</param>
public sealed partial class KeyframeThumbnails(ILogger<KeyframeThumbnails> logger) : IKeyframeThumbnails
{
    /// <inheritdoc />
    public Task StoreAsync(LibraryLayout library, Id<MediaItem> itemId, IReadOnlyList<SampledFrame> frames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(frames);

        return Task.Run(
            () =>
            {
                try
                {
                    DeleteAll(library, itemId);
                    foreach (var frame in frames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Write(library.GetKeyframeThumbnailPath(itemId, frame.Index), frame);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogStoreFailed(logger, itemId.Value, ex);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public string? Find(LibraryLayout library, Id<MediaItem> itemId, int index)
    {
        ArgumentNullException.ThrowIfNull(library);

        var path = library.GetKeyframeThumbnailPath(itemId, index);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Deletes all of an item's keyframe pictures.
    /// </summary>
    /// <param name="library">The library's folder layout.</param>
    /// <param name="itemId">The item.</param>
    internal static void DeleteAll(LibraryLayout library, Id<MediaItem> itemId)
    {
        var folder = Path.GetDirectoryName(library.GetKeyframeThumbnailPath(itemId, 0))!;
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, LibraryLayout.KeyframeThumbnailSearchPattern(itemId)))
        {
            File.Delete(file);
        }
    }

    private static void Write(string path, SampledFrame frame)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        frame.Pixels.Span.CopyTo(bitmap.GetPixelSpan());
        using var scaled = ThumbnailEncoder.ScaleDown(bitmap, IKeyframeThumbnails.LongestEdge);
        using var data = (scaled ?? bitmap).Encode(SKEncodedImageFormat.Webp, ThumbnailEncoder.Quality)
            ?? throw new IOException("The keyframe picture couldn't be encoded.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporary))
            {
                data.SaveTo(stream);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't store the keyframe pictures of item {ItemId}")]
    private static partial void LogStoreFailed(ILogger logger, Guid itemId, Exception exception);
}
