using FFMpegCore;
using FFMpegCore.Pipes;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Media.Video;

/// <summary>
/// Reads single video frames with the local FFmpeg (D068), for thumbnails (E03-S04-T03) and keyframes (E06-S02).
/// </summary>
internal static class FfmpegFrameReader
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads the frame at a position, seeking to it exactly. FFmpeg turns the frame upright by the display rotation and
    /// scales it down, never up, so its longest edge is at most <paramref name="longestEdge"/>.
    /// </summary>
    /// <param name="ffmpegFolder">Folder with the <c>ffmpeg</c> executable.</param>
    /// <param name="path">The video file.</param>
    /// <param name="position">Where to read.</param>
    /// <param name="longestEdge">The longest edge allowed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame, or <see langword="null"/> when FFmpeg wrote none, e.g. past the last frame.</returns>
    public static async Task<SKBitmap?> ReadFrameAsync(string ffmpegFolder, string path, TimeSpan position, int longestEdge, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FrameTimeout);
        using var output = new MemoryStream();

        // FFmpeg applies the rotation before the filter, so the limits are on the upright frame.
        await FFMpegArguments
            .FromFileInput(path, verifyExists: true, options => options.Seek(position))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .WithFrameOutputCount(1)
                .WithCustomArgument($"-vf \"scale='min({longestEdge},iw)':'min({longestEdge},ih)':force_original_aspect_ratio=decrease\"")
                .WithVideoCodec("bmp")
                .ForceFormat("image2pipe"))
            .CancellableThrough(timeout.Token)
            .ProcessAsynchronously(throwOnError: true, new FFOptions { BinaryFolder = ffmpegFolder })
            .ConfigureAwait(false);
        if (output.Length == 0)
        {
            return null;
        }

        using var data = SKData.CreateCopy(output.GetBuffer().AsSpan(0, (int)output.Length));
        using var codec = SkiaDecoding.OpenCodec(data);
        return SkiaDecoding.DecodeStill(codec, longestEdge, longestEdge);
    }
}
