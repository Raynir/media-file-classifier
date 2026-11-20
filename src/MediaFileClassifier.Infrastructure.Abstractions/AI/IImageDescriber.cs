namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Describes an image: title, description, summary, tags, objects, scenes, actions and emotions.
/// </summary>
public interface IImageDescriber : IAiProvider
{
    /// <summary>
    /// Gets the version of the prompt and output contract used when the profile asks for none. The pipeline stores the
    /// version used with each analysis run.
    /// </summary>
    string PromptVersion { get; }

    /// <summary>
    /// Gets the prompt version a request with <see cref="ImageDescriptionOptions.PromptVersion"/> set to
    /// <paramref name="requested"/> would use (E06-S04-T04), so outdated detection (D358) compares with the same one.
    /// </summary>
    /// <param name="requested">The analysis profile's prompt version, or <c>null</c>.</param>
    /// <returns>The version, <see cref="PromptVersion"/> when the describer has no such template.</returns>
    string GetPromptVersion(string? requested);

    /// <summary>
    /// Gets the fields this describer can produce (E06-S04-T03). The pipeline ignores any other field the describer
    /// returns, so those stay empty.
    /// </summary>
    DescriberFields SupportedFields { get; }

    /// <summary>
    /// Describes one image.
    /// </summary>
    /// <param name="image">The image, or a representative frame of a video.</param>
    /// <param name="options">Request options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated description.</returns>
    /// <exception cref="AiProviderException">The call failed, or the output stayed invalid after repair and retry.</exception>
    Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken);
}
