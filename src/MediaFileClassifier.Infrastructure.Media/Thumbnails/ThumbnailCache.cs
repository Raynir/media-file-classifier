using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Media.Thumbnails;

/// <summary>
/// Stores thumbnails as files in the library's sharded cache folder (E03-S04-T04, D078). The state is the files
/// themselves, so there is no index to keep in step: a thumbnail exists when its file does, and is stale when the file
/// was written before the item's file last changed.
/// </summary>
/// <param name="timeProvider">Clock, for the time stamp of new thumbnails.</param>
/// <param name="logger">Logger.</param>
public sealed partial class ThumbnailCache(TimeProvider timeProvider, ILogger<ThumbnailCache> logger) : IThumbnailCache, IDisposable
{
    /// <summary>
    /// Limits generations running at once to half the cores, so imports leave the UI and other work room until the
    /// job system (E05) schedules them.
    /// </summary>
    private static readonly TimeSpan FileTimeResolution = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim gate = new(MaxConcurrency, MaxConcurrency);

    /// <summary>
    /// Gets how many generations run at once.
    /// </summary>
    public static int MaxConcurrency { get; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <inheritdoc />
    public string? Find(LibraryLayout library, Id<MediaItem> itemId, ThumbnailSize size, DateTimeOffset sourceModifiedAt)
    {
        ArgumentNullException.ThrowIfNull(library);

        var file = new FileInfo(library.GetThumbnailPath(itemId, size));
        return file.Exists && file.LastWriteTimeUtc >= sourceModifiedAt.UtcDateTime ? file.FullName : null;
    }

    /// <inheritdoc />
    public async Task<ThumbnailOutcome> GenerateAsync(
        LibraryLayout library,
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);
        var generator = format is { IsSupported: true, Handler: { } handler }
            ? handler.ThumbnailGenerator
            : throw new ArgumentException("The format isn't supported, so it has no thumbnail generator.", nameof(format));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Decoding and encoding are synchronous CPU work, so they run on the thread pool, never on the caller's.
            var thumbnails = await Task.Run(() => generator.GenerateAsync(content, format, metadata, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (!thumbnails.HasImages)
            {
                DeleteThumbnails(library, content.ItemId);
                return new(IsStored: false, thumbnails.Warnings);
            }

            // Stamped no earlier than the item's file, so a file time in the future doesn't leave the thumbnail stale. A
            // future time gets a margin, since FAT volumes round file times to 2 seconds.
            var now = timeProvider.GetUtcNow();
            var stamp = (content.LastModifiedUtc > now ? content.LastModifiedUtc + FileTimeResolution : now).UtcDateTime;
            foreach (var (size, image) in thumbnails.Images)
            {
                await WriteAsync(library.GetThumbnailPath(content.ItemId, size), image, stamp, cancellationToken).ConfigureAwait(false);
            }

            return new(IsStored: true, thumbnails.Warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(logger, content.ItemId.Value, ex);
            DeleteThumbnails(library, content.ItemId);
            return new(IsStored: false, [new ExtractionWarning(ThumbnailWarnings.ThumbnailFailed, ex.Message)]);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate(LibraryLayout library, Id<MediaItem> itemId)
    {
        ArgumentNullException.ThrowIfNull(library);

        DeleteThumbnails(library, itemId);

        // The keyframe pictures of a video or animated image (E14-S13) go with its thumbnails.
        try
        {
            KeyframeThumbnails.DeleteAll(library, itemId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(logger, itemId.Value, ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    // Only the item's own thumbnails. The keyframe pictures are made by the analysis, not by this generation, so a
    // thumbnail that failed doesn't take them (MFC-447).
    private void DeleteThumbnails(LibraryLayout library, Id<MediaItem> itemId)
    {
        foreach (var size in Enum.GetValues<ThumbnailSize>())
        {
            try
            {
                File.Delete(library.GetThumbnailPath(itemId, size));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file may be open in the grid; it is replaced or found stale later.
                LogDeleteFailed(logger, itemId.Value, ex);
            }
        }
    }

    /// <summary>
    /// Writes a thumbnail through a temporary file, so a reader never sees half a file.
    /// </summary>
    private static async Task WriteAsync(string path, byte[] image, DateTime stamp, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, image, cancellationToken).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(temporary, stamp);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't store the thumbnails of item {ItemId}")]
    private static partial void LogWriteFailed(ILogger logger, Guid itemId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete a thumbnail of item {ItemId}")]
    private static partial void LogDeleteFailed(ILogger logger, Guid itemId, Exception exception);
}
