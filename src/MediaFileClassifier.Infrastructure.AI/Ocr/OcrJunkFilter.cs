namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// Tells a stray mark the recogniser reads out of texture or a pattern ("A", "**", "XX") from real text (E06-S05-T02).
/// </summary>
internal static class OcrJunkFilter
{
    /// <summary>
    /// Decides whether a recognised line is kept. A line with fewer than <see cref="RapidOcrOptions.MinTextCharacters"/>
    /// letters and digits is dropped, and so is a short one, up to <see cref="RapidOcrOptions.ShortTextCharacters"/>, that
    /// isn't read with at least <see cref="RapidOcrOptions.MinShortTextConfidence"/>.
    /// </summary>
    /// <param name="text">The recognised text.</param>
    /// <param name="confidence">The recogniser's confidence.</param>
    /// <param name="options">The thresholds.</param>
    /// <returns><see langword="true"/> to keep the line.</returns>
    public static bool Keep(string text, float confidence, RapidOcrOptions options)
    {
        if (confidence < options.MinTextConfidence)
        {
            return false;
        }

        var characters = text.Count(char.IsLetterOrDigit);
        if (characters < options.MinTextCharacters)
        {
            return false;
        }

        return characters > options.ShortTextCharacters || confidence >= options.MinShortTextConfidence;
    }
}
