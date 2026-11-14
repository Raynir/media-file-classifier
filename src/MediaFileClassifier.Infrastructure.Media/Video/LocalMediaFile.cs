using MediaFileClassifier.Infrastructure.Abstractions.Metadata;

namespace MediaFileClassifier.Infrastructure.Media.Video;

/// <summary>
/// A file FFmpeg can open by path: the item's own file, or a temporary copy of a content stream, since FFmpeg needs to
/// seek in an MP4.
/// </summary>
internal sealed class LocalMediaFile : IAsyncDisposable
{
    private readonly bool isTemporary;

    private LocalMediaFile(string path, bool isTemporary)
    {
        Path = path;
        this.isTemporary = isTemporary;
    }

    /// <summary>
    /// Gets the file's path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a local file for the content, copying the stream to a temporary file when there's no path.
    /// </summary>
    /// <param name="content">The file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local file. Disposing it deletes a temporary copy.</returns>
    public static async Task<LocalMediaFile> CreateAsync(MediaContent content, CancellationToken cancellationToken)
    {
        if (content.LocalPath is { } path)
        {
            return new(path, isTemporary: false);
        }

        var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mfc-video-{Guid.NewGuid():N}.mp4");
        try
        {
            var source = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var target = File.Create(temporary);
                await using (target.ConfigureAwait(false))
                {
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }

            return new(temporary, isTemporary: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (isTemporary)
        {
            File.Delete(Path);
        }

        return ValueTask.CompletedTask;
    }
}
