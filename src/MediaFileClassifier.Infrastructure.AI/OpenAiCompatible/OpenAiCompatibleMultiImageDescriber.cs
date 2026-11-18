using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// A <see cref="OpenAiCompatibleImageDescriber"/> for a model that takes several pictures in one message, so a clip's
/// keyframes go in one request (E06-S04-T07).
/// </summary>
public sealed class OpenAiCompatibleMultiImageDescriber : OpenAiCompatibleImageDescriber, IMultiImageDescriber
{
    /// <summary>
    /// The most pictures a request carries by default, for the models <see cref="VisionModelDefaults"/> knows take
    /// several (E06-S04-T07).
    /// </summary>
    public const int DefaultMaxImages = 6;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleMultiImageDescriber"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="displayName">Name shown to the user.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="httpClient">HTTP client, or <c>null</c> for the SDK's own.</param>
    /// <param name="useSchemaResponseFormat">Whether to send the schema as <c>response_format</c>.</param>
    /// <param name="maxImages">The most pictures one request may carry, at least 2; see <see cref="DefaultMaxImages"/>.</param>
    public OpenAiCompatibleMultiImageDescriber(
        string id,
        string displayName,
        OpenAiCompatibleEndpoint endpoint,
        HttpClient? httpClient = null,
        bool useSchemaResponseFormat = true,
        int maxImages = DefaultMaxImages)
        : base(id, displayName, endpoint, httpClient, useSchemaResponseFormat)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxImages, 2);
        MaxImages = maxImages;
    }

    /// <inheritdoc />
    public int MaxImages { get; }

    /// <inheritdoc />
    public Task<AiOutput<ImageDescription>> DescribeFramesAsync(IReadOnlyList<AiImage> frames, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);
        if (frames.Count == 0 || frames.Count > MaxImages)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), frames.Count, $"A request carries 1 to {MaxImages} pictures.");
        }

        return DescribeCoreAsync(frames, options, cancellationToken);
    }
}
