namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Recognises the ways a vision-language model says an image has no text, other than the marker it was asked to use
/// (E06-S05-T02), so its own remark isn't stored as the picture's text.
/// </summary>
internal static class OcrNoTextAnswer
{
    private static readonly HashSet<string> Phrases = new(StringComparer.Ordinal)
    {
        "no text",
        "no text found",
        "no text visible",
        "no text detected",
        "no visible text",
        "no text in the image",
        "no text in this image",
        "there is no text",
        "there is no text in the image",
        "there is no text in this image",
        "there is no visible text",
        "there is no visible text in the image",
        "there is no visible text in this image",
        "the image contains no text",
        "this image contains no text",
        "the image has no text",
        "the image does not contain any text",
        "the image doesnt contain any text",
        "no text is visible",
        "no text is visible in the image",
        "no text is visible in this image",
        "none",
        "n/a",
        "nothing",
    };

    /// <summary>
    /// Checks whether an answer means "no text".
    /// </summary>
    /// <param name="answer">The model's answer, trimmed.</param>
    /// <returns><see langword="true"/> if the model says there's no text.</returns>
    public static bool Matches(string answer)
    {
        if (answer.Length is 0 or > 80 || answer.Contains('\n'))
        {
            return answer.Length == 0;
        }

        var key = new string([.. answer.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '/')]);
        return Phrases.Contains(string.Join(' ', key.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }
}
