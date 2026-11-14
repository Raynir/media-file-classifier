namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// Escapes text for SQL <c>LIKE</c> patterns, so <c>%</c> and <c>_</c> in a folder name or format match themselves.
/// Use with <c>ESCAPE '\'</c>.
/// </summary>
internal static class SqlLike
{
    /// <summary>
    /// The escape character.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    /// Escapes the wildcard and escape characters of a text.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The text, matching itself in a pattern.</returns>
    public static string Escape(string value) =>
        value.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);

    /// <summary>
    /// Makes the pattern of everything inside a subfolder, in upper case: match it against the path folded with
    /// <see cref="LibraryConnectionFactory.FoldCase"/>, so folder names ignore case in every alphabet (D310).
    /// </summary>
    /// <param name="subfolder">The subfolder, with <c>/</c> or <c>\</c> separators; <c>null</c> or empty for none.</param>
    /// <returns>The pattern, or <c>null</c> if there's no subfolder.</returns>
    public static string? SubfolderPattern(string? subfolder) =>
        subfolder?.Replace('\\', '/').Trim('/') is { Length: > 0 } trimmed ? Escape(trimmed.ToUpperInvariant()) + "/%" : null;
}
