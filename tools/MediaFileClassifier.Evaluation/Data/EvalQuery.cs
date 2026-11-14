namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// A search query with every relevant item of the data set: an entry of <c>queries.json</c>.
/// Relevance is binary.
/// </summary>
public sealed record EvalQuery
{
    public required string Id { get; init; }

    /// <summary>
    /// <c>en</c> or <c>ru</c> (D029).
    /// </summary>
    public required string Language { get; init; }

    public required string Text { get; init; }

    /// <summary>
    /// What the query tests, one of <see cref="QueryCategories.All"/>.
    /// </summary>
    public required string Category { get; init; }

    public required IReadOnlyList<string> Relevant { get; init; }

    /// <summary>
    /// Free text for reviewers, e.g. why an item is or is not relevant.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// The query categories. Hard cases get their own category so they can be read separately.
/// </summary>
public static class QueryCategories
{
    public static IReadOnlyList<string> All { get; } =
    [
        "object",      // a thing or animal: "a cat"
        "scene",       // a place or situation: "a crowded beach"
        "synonym",     // a word the descriptions don't use: "kitty", "автомобиль"
        "text",        // text visible in the image: "a sign that says Bergen St"
        "abstract",    // a mood or idea: "loneliness", "celebration"
        "distractor",  // look-alikes must rank below the real thing: "a cat, not a lynx"
        "media",       // the kind of file: "an animated GIF of a flag", "a screenshot of an error"
        "filename",    // a fragment of the file name: "Pelmeni", "2016-10-03" (E09-S05)
        "tag",         // a one-word, tag-style query: "autumn", "чёрно-белое" (E09-S05)
        None,          // nothing in the data set should match: "a giraffe" (E09-S05); no relevant items
    ];

    /// <summary>
    /// The category of queries that should return nothing. They have no relevant items, so they're left out of
    /// recall@10 and MRR and scored by whether any result is shown.
    /// </summary>
    public const string None = "none";
}
