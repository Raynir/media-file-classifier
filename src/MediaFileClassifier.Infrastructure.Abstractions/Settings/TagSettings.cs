namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Overrides for AI tag normalisation (E06-S04-T05), edited in <c>settings.json</c>; the app has no UI for them.
/// A list left out (<c>null</c>) uses the built-in defaults, and a list given replaces them.
/// </summary>
public sealed record TagSettings
{
    /// <summary>
    /// Gets the generic tags dropped from AI output, such as "photo", or <c>null</c> for the defaults. Compared after
    /// normalisation, ignoring case.
    /// </summary>
    public IReadOnlyList<string>? Stopwords { get; init; }

    /// <summary>
    /// Gets the synonym map, tag to the tag it becomes (e.g. "automobile" to "car"), or <c>null</c> for the defaults.
    /// Keys are compared after normalisation, ignoring case.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Synonyms { get; init; }
}
