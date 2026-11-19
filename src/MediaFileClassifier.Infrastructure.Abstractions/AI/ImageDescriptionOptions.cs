namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Options for one description request.
/// </summary>
/// <param name="Language">Language to write the text in, as an ISO 639-1 code, or <c>null</c> for English.</param>
/// <param name="Context">Extra context for the model, such as the file name or the folder it's in.</param>
/// <param name="PromptVersion">The prompt template version to use (E06-S04-T04), or <c>null</c> for the describer's default.</param>
public sealed record ImageDescriptionOptions(string? Language = null, string? Context = null, string? PromptVersion = null)
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static ImageDescriptionOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of pictures in the request: 1 for an image, more for frames of one clip (E06-S04-T07), which the
    /// prompt then describes as a whole.
    /// </summary>
    public int FrameCount { get; init; } = 1;
}
