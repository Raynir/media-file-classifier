namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Text found in an image.
/// </summary>
/// <param name="Text">The text, with line breaks between lines. Empty if the image has no text.</param>
/// <param name="Language">Detected language as an ISO 639-1 code, if the provider reports it.</param>
public sealed record OcrResult(string Text, string? Language = null)
{
    /// <summary>
    /// Gets a value indicating whether any text was found.
    /// </summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
