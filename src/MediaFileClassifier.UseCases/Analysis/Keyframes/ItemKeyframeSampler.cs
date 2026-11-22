using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediaFileClassifier.UseCases.Embeddings;

namespace MediaFileClassifier.UseCases.Analysis.Keyframes;

/// <summary>
/// The one place that samples a video's or animated image's keyframes for analysis (E06-S02) and stores them: their
/// rows replace the item's keyframes, their pictures replace the item's keyframe pictures (E14-S13, D227), and their
/// pixels come back ready for the analyzers. Each call samples the media once, so a caller that needs the frames more
/// than once keeps the result (<see cref="AnalysisInputs"/> does, for all of an item's analyzers).
/// </summary>
/// <param name="frameSampler">Samples the keyframes.</param>
/// <param name="keyframeThumbnails">Stores the keyframes' pictures for the preview's strip, or <see langword="null"/> for none.</param>
public sealed class ItemKeyframeSampler(IFrameSampler frameSampler, IKeyframeThumbnails? keyframeThumbnails = null)
{
    private readonly IFrameSampler frameSampler = frameSampler ?? throw new ArgumentNullException(nameof(frameSampler));

    /// <summary>
    /// Samples an item's keyframes and stores them with their pictures. With a sampling warning there may be no
    /// frames, and then nothing is stored: the item's earlier keyframes and pictures stay.
    /// </summary>
    /// <param name="library">The open library, where the keyframes and their pictures are stored.</param>
    /// <param name="content">The item's content.</param>
    /// <param name="format">The detected format.</param>
    /// <param name="metadata">The item's technical metadata (duration, orientation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored keyframes with their pixels, in time order, and the sampling warnings.</returns>
    public async Task<FrameInputs> SampleAndStoreAsync(
        IOpenLibrary library,
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metadata);

        var sample = await frameSampler.SampleAsync(content, format, metadata, FrameSamplingOptions.Default, cancellationToken).ConfigureAwait(false);
        if (sample.Frames.Count == 0)
        {
            return new FrameInputs([], sample.Warnings);
        }

        var keyframes = sample.Frames.Select(frame => frame.ToKeyframe(content.ItemId)).ToList();
        await library.Keyframes.ReplaceAsync(content.ItemId, keyframes, cancellationToken).ConfigureAwait(false);
        if (keyframeThumbnails is not null)
        {
            await keyframeThumbnails.StoreAsync(library.Layout, content.ItemId, sample.Frames, cancellationToken).ConfigureAwait(false);
        }

        return new FrameInputs(
            [.. keyframes.Zip(sample.Frames, (keyframe, frame) => new KeyframeImage(keyframe, AiImage.FromRgba(frame.Pixels, frame.Width, frame.Height, $"frame {frame.Index}")))],
            sample.Warnings);
    }
}
