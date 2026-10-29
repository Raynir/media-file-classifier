using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Searches;

/// <summary>
/// A search the user saved to run again. The feature is Later; only the shape is reserved.
/// </summary>
/// <remarks>
/// Filters are stored as JSON, so the filter model (E10) can grow without changing this entity.
/// </remarks>
public sealed record SavedSearch
{
    public required Id<SavedSearch> Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Gets the plain-text query (D022).
    /// </summary>
    public required string QueryText { get; init; }

    public string? FiltersJson { get; init; }

    public SortMode Sort { get; init; } = SortMode.Relevance;

    public required DateTimeOffset CreatedAt { get; init; }
}
