using System.Runtime.ExceptionServices;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.UseCases.Embeddings;

namespace MediaFileClassifier.UseCases.Analysis.Pipeline;

/// <summary>
/// One item's prepared inputs, shared by its analyzers so the media is decoded or sampled only once (E06-S01-T02).
/// A still image is read and decoded on first use, at the size every enabled analyzer needs together, and each
/// analyzer gets a copy shrunk to its own size. A video or animated image is sampled once (E06-S02) and its keyframes
/// are stored once. Analyzers of one item run one after another, so this isn't thread-safe.
/// </summary>
public sealed class AnalysisInputs
{
    private readonly IAnalysisImageDecoder decoder;
    private readonly IFrameSampler frameSampler;
    private readonly IOpenLibrary library;
    private readonly IKeyframeThumbnails? keyframeThumbnails;
    private readonly InputSize decodeSize;
    private readonly Dictionary<int, DecodedImage> shrunk = [];
    private DecodedImage? still;
    private ExceptionDispatchInfo? stillFailure;
    private FrameInputs? frames;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisInputs"/> class.
    /// </summary>
    /// <param name="decoder">Still image decoder.</param>
    /// <param name="frameSampler">Keyframe sampler.</param>
    /// <param name="library">The open library, where keyframes are stored.</param>
    /// <param name="content">The item's content.</param>
    /// <param name="format">The detected format.</param>
    /// <param name="metadata">The item's technical metadata (duration, orientation).</param>
    /// <param name="decodeSize">The size a still image is decoded at: what the enabled analyzers need together.</param>
    /// <param name="keyframeThumbnails">Stores the keyframes' pictures for the preview's strip, or <see langword="null"/> for none.</param>
    public AnalysisInputs(
        IAnalysisImageDecoder decoder,
        IFrameSampler frameSampler,
        IOpenLibrary library,
        MediaContent content,
        MediaFormatDetection format,
        TechnicalMetadata metadata,
        InputSize decodeSize,
        IKeyframeThumbnails? keyframeThumbnails = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(frameSampler);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(decodeSize);

        this.decoder = decoder;
        this.frameSampler = frameSampler;
        this.library = library;
        this.keyframeThumbnails = keyframeThumbnails;
        this.decodeSize = decodeSize;
        Content = content;
        Format = format;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the item's content.
    /// </summary>
    public MediaContent Content { get; }

    /// <summary>
    /// Gets the detected format.
    /// </summary>
    public MediaFormatDetection Format { get; }

    /// <summary>
    /// Gets the item's technical metadata.
    /// </summary>
    public TechnicalMetadata Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether the item is analyzed from keyframes (video and animated images, D013).
    /// </summary>
    public bool HasFrames => Format.MediaType is MediaType.Video or MediaType.AnimatedImage;

    /// <summary>
    /// Gets the still image, decoded once, shrunk to at most <paramref name="maxLongestEdge"/> if given.
    /// </summary>
    /// <param name="maxLongestEdge">The analyzer's largest longest edge, or <c>null</c> for the shared decode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The picture.</returns>
    public async Task<DecodedImage> GetStillAsync(int? maxLongestEdge, CancellationToken cancellationToken)
    {
        if (HasFrames)
        {
            throw new InvalidOperationException("A video or animated image is analyzed from its keyframes.");
        }

        // A file that can't be read or decoded fails every analyzer with the same exception, without reading it again.
        stillFailure?.Throw();
        if (still is null)
        {
            try
            {
                var bytes = await ItemEmbeddingAnalyzer.ReadAsync(Content, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                still = decoder.Decode(bytes, decodeSize.MinShortSide, decodeSize.MinLongestEdge, Path.GetFileName(Content.LocalPath));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stillFailure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }

        if (maxLongestEdge is not { } edge || still.LongestEdge <= edge)
        {
            return still;
        }

        if (!shrunk.TryGetValue(edge, out var image))
        {
            image = decoder.Shrink(still, edge);
            shrunk[edge] = image;
        }

        return image;
    }

    /// <summary>
    /// Shrinks a keyframe so its longest edge is at most <paramref name="maxLongestEdge"/>, e.g. to send several to a
    /// describer at once. A smaller frame is returned as it is.
    /// </summary>
    /// <param name="frame">A raw RGBA keyframe from <see cref="GetFramesAsync"/>.</param>
    /// <param name="maxLongestEdge">The longest edge.</param>
    /// <returns>The frame, with its name.</returns>
    public AiImage ShrinkFrame(AiImage frame, int maxLongestEdge)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.IsRawRgba || frame.PixelWidth is not { } width || frame.PixelHeight is not { } height || Math.Max(width, height) <= maxLongestEdge)
        {
            return frame;
        }

        return decoder.Shrink(new DecodedImage(frame.Data, width, height), maxLongestEdge).ToAiImage(frame.Name);
    }

    /// <summary>
    /// Gets the keyframes, sampled and stored once. With a sampling warning there may be none.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The keyframes, with their pixels, and the sampling warnings.</returns>
    public async Task<FrameInputs> GetFramesAsync(CancellationToken cancellationToken)
    {
        if (!HasFrames)
        {
            throw new InvalidOperationException("A still image has no keyframes.");
        }

        if (frames is not null)
        {
            return frames;
        }

        var sample = await frameSampler.SampleAsync(Content, Format, Metadata, FrameSamplingOptions.Default, cancellationToken).ConfigureAwait(false);
        if (sample.Frames.Count == 0)
        {
            frames = new FrameInputs([], sample.Warnings);
            return frames;
        }

        var keyframes = sample.Frames.Select(frame => frame.ToKeyframe(Content.ItemId)).ToList();
        await library.Keyframes.ReplaceAsync(Content.ItemId, keyframes, cancellationToken).ConfigureAwait(false);
        if (keyframeThumbnails is not null)
        {
            await keyframeThumbnails.StoreAsync(library.Layout, Content.ItemId, sample.Frames, cancellationToken).ConfigureAwait(false);
        }

        frames = new FrameInputs(
            [.. keyframes.Zip(sample.Frames, (keyframe, frame) => new KeyframeImage(keyframe, AiImage.FromRgba(frame.Pixels, frame.Width, frame.Height, $"frame {frame.Index}")))],
            sample.Warnings);
        return frames;
    }
}

/// <summary>
/// An item's keyframes, ready for the analyzers.
/// </summary>
/// <param name="Frames">The stored keyframes and their pixels, in time order.</param>
/// <param name="Warnings">Sampling warnings, e.g. FFmpeg missing.</param>
public sealed record FrameInputs(IReadOnlyList<KeyframeImage> Frames, IReadOnlyList<ExtractionWarning> Warnings);

/// <summary>
/// How large a decoded still image must be. A decode meets every enabled analyzer's size together.
/// </summary>
/// <param name="MinShortSide">The smallest short side.</param>
/// <param name="MinLongestEdge">The smallest longest edge.</param>
public sealed record InputSize(int MinShortSide, int MinLongestEdge)
{
    /// <summary>
    /// Combines two sizes into one that meets both.
    /// </summary>
    /// <param name="other">The other size.</param>
    /// <returns>The larger of each limit.</returns>
    public InputSize Union(InputSize other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new InputSize(Math.Max(MinShortSide, other.MinShortSide), Math.Max(MinLongestEdge, other.MinLongestEdge));
    }
}
