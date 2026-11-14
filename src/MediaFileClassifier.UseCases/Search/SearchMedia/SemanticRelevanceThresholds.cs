using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.UseCases.Search.SearchMedia;

/// <summary>
/// The semantic relevance threshold of each text embedding model (E09-S01-T03), picked from the evaluation corpus
/// (E01-S10) with <c>tools/MediaFileClassifier.Evaluation threshold</c>, and tuned again in E09-S05.
/// </summary>
public static class SemanticRelevanceThresholds
{
    /// <summary>
    /// SigLIP 2 base-patch16-256 (D092). On the corpus (345 items, 215 English and Russian queries, 20 nonsense
    /// queries): 88% (en) and 89% (ru) of the relevant items in the top 10 stay visible, 96% and 98% of real queries
    /// keep a result, and 2 of 20 nonsense queries show one item. No nonsense query reached a raw similarity of 0.13.
    /// The E09-S05 tuning pass (351 items, 281 queries) kept it: 0.05 / 0.12 raised hybrid recall@10 (en 0.93 to
    /// 0.97, ru 0.74 to 0.80) but 16 of 20 nonsense queries then showed results; 0.09 / 0.15 hid every nonsense query
    /// but dropped recall to 0.88 (en) and 0.58 (ru).
    /// </summary>
    public static SemanticRelevanceCut Siglip2Base { get; } = new(MinCentredSimilarity: 0.07f, MinSimilarity: 0.13f);

    private static readonly Dictionary<string, SemanticRelevanceCut> ByModel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["siglip2-base-patch16-256"] = Siglip2Base,
    };

    /// <summary>
    /// Finds the threshold of a model.
    /// </summary>
    /// <param name="model">The text embedding model.</param>
    /// <returns>The threshold, or <c>null</c> for a model nobody measured: its hits are all shown.</returns>
    public static SemanticRelevanceCut? For(ModelIdentity model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return ByModel.GetValueOrDefault(model.ModelName);
    }
}
