namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// When a vector match counts as relevant (E09-S01-T03). Vector search always returns some nearest neighbours, even
/// for nonsense, so a hit that only the vector search found is "less relevant" unless its similarity clears either
/// bar. Keyword matches are always relevant.
/// </summary>
/// <remarks>
/// Raw cosine similarity alone separates real and nonsense queries poorly, because some queries sit closer to every
/// picture than others. The centred similarity subtracts the query's mean similarity over the library's items, which
/// removes that offset. The raw floor keeps strong matches in a library of one subject, where every item is close to
/// the mean.
/// </remarks>
/// <param name="MinCentredSimilarity">The least similarity minus the query's mean similarity over the library.</param>
/// <param name="MinSimilarity">The raw cosine similarity that is relevant whatever the library holds.</param>
public sealed record SemanticRelevanceCut(float MinCentredSimilarity, float MinSimilarity)
{
    /// <summary>
    /// Checks whether a similarity is relevant.
    /// </summary>
    /// <param name="similarity">The hit's cosine similarity.</param>
    /// <param name="librarySimilarity">The query's mean similarity over the library's items.</param>
    /// <returns><c>true</c> if the hit is relevant.</returns>
    public bool IsRelevant(float similarity, float librarySimilarity) =>
        similarity >= MinSimilarity || similarity - librarySimilarity >= MinCentredSimilarity;

    /// <summary>
    /// Checks that both bars are finite numbers.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A bar is NaN or infinite.</exception>
    public void Validate()
    {
        if (!float.IsFinite(MinCentredSimilarity))
        {
            throw new ArgumentOutOfRangeException(nameof(MinCentredSimilarity), MinCentredSimilarity, "The bar must be a finite number.");
        }

        if (!float.IsFinite(MinSimilarity))
        {
            throw new ArgumentOutOfRangeException(nameof(MinSimilarity), MinSimilarity, "The bar must be a finite number.");
        }
    }
}
