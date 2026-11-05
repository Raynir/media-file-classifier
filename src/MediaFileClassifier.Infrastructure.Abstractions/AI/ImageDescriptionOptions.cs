namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Options for one description request.
/// </summary>
/// <param name="Language">Language to write the text in, as an ISO 639-1 code, or <c>null</c> for English.</param>
/// <param name="Context">Extra context for the model, such as the file name or the folder it's in.</param>
public sealed record ImageDescriptionOptions(string? Language = null, string? Context = null)
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static ImageDescriptionOptions Default { get; } = new();
}
