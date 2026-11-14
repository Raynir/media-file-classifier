namespace MediaFileClassifier.Infrastructure.Search.Ranking;

/// <summary>
/// Splits plain query text (D022) into the words the keyword index matches.
/// </summary>
internal static class QueryTerms
{
    /// <summary>
    /// The most words a query uses; the rest are ignored, so a pasted paragraph can't build a huge query.
    /// </summary>
    public const int MaxTerms = 16;

    /// <summary>
    /// Splits text on white space. Words without a letter or digit can't match anything and are dropped, and so are
    /// repeats, ignoring case.
    /// </summary>
    /// <param name="text">The query text, or <c>null</c>.</param>
    /// <returns>The words, in order.</returns>
    public static IReadOnlyList<string> Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(word => word.Any(char.IsLetterOrDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxTerms)];
}
