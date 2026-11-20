namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// A describer that takes several pictures in one request (E06-S04-T07), such as a vision-language model, so a video
/// gets one description of the whole clip from its keyframes. Describers without it are asked frame by frame.
/// </summary>
public interface IMultiImageDescriber : IImageDescriber
{
    /// <summary>
    /// Gets the most pictures one request may carry, at least 2.
    /// </summary>
    int MaxImages { get; }

    /// <summary>
    /// Describes the frames of one clip, in time order, as a whole.
    /// </summary>
    /// <param name="frames">The frames, at most <see cref="MaxImages"/>.</param>
    /// <param name="options">Request options; the context says they are frames of one clip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated description of the clip.</returns>
    /// <exception cref="AiProviderException">The call failed, or the output stayed invalid after repair and retry.</exception>
    Task<AiOutput<ImageDescription>> DescribeFramesAsync(IReadOnlyList<AiImage> frames, ImageDescriptionOptions options, CancellationToken cancellationToken);
}
