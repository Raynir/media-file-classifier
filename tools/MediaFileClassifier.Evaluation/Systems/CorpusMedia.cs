using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// Reads corpus files the way the app's analysis does: a still image as its bytes, a video or animated image as the
/// keyframes of the app's <c>FrameSampler</c> (videos need FFmpeg).
/// </summary>
internal sealed class CorpusMedia : IDisposable
{
    private readonly ServiceProvider services;

    public CorpusMedia()
    {
        var capabilities = new CapabilityService([], NullLogger<CapabilityService>.Instance);
        capabilities.SetAvailable(KnownCapabilities.VideoAnalysis);
        services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ICapabilityService>(capabilities)
            .AddSingleton<IPerceptualHasher, SkiaPerceptualHasher>()
            .AddMedia()
            .AddAi()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Gets the app's image decoder for analysis inputs.
    /// </summary>
    public IAnalysisImageDecoder Decoder => services.GetRequiredService<IAnalysisImageDecoder>();

    /// <summary>
    /// Reads a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The keyframes of a video or animated image (maybe none), or the bytes of a still image.</returns>
    public async Task<CorpusMediaFile> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var content = new MediaContent(Id<MediaItem>.New(), DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(File.OpenRead(path))) { LocalPath = path };
        MediaFormatDetection format;
        await using (var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false))
        {
            format = await services.GetRequiredService<IMediaFormatRegistry>().DetectAsync(stream, Path.GetFileName(path), cancellationToken).ConfigureAwait(false);
        }

        if (!format.IsSupported)
        {
            throw new InvalidDataException($"{path} isn't a supported media file: {format.Rejection}");
        }

        if (format.MediaType is MediaType.Video or MediaType.AnimatedImage)
        {
            var metadata = await format.Handler!.MetadataExtractor.ExtractAsync(content, format, cancellationToken).ConfigureAwait(false);
            var sample = await services.GetRequiredService<IFrameSampler>().SampleAsync(content, format, metadata, FrameSamplingOptions.Default, cancellationToken).ConfigureAwait(false);
            return new CorpusMediaFile([.. sample.Frames.Select(frame => AiImage.FromRgba(frame.Pixels, frame.Width, frame.Height, $"frame {frame.Index}"))], null);
        }

        return new CorpusMediaFile(null, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public void Dispose() => services.Dispose();
}

/// <summary>
/// A corpus file as the analysis sees it.
/// </summary>
/// <param name="Frames">The keyframes of a video or animated image, or <c>null</c> for a still image.</param>
/// <param name="Bytes">The encoded still image, or <c>null</c> for a video or animated image.</param>
internal sealed record CorpusMediaFile(IReadOnlyList<AiImage>? Frames, byte[]? Bytes)
{
    public bool HasFrames => Frames is not null;
}
