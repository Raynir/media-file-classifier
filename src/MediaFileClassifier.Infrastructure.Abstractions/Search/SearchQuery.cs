using MediaFileClassifier.Domain.Searches;

namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// A search: optional text, optional vector, filters, order and page. The mode follows from what's given:
/// text only is a keyword search, a vector only is a vector search, both is a hybrid search, and neither lists the
/// filtered items.
/// </summary>
public sealed record SearchQuery
{
    /// <summary>
    /// The largest page.
    /// </summary>
    public const int MaxLimit = 1000;

    /// <summary>
    /// Gets the plain-text query (D022), or <c>null</c>. Words are separated by white space; punctuation inside a word
    /// is kept, so <c>IMG_0042</c> is one word.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the query vector with its model, or <c>null</c>.
    /// </summary>
    public SearchVector? Vector { get; init; }

    /// <summary>
    /// Gets the structured filters. They apply to every mode.
    /// </summary>
    public SearchFilter Filter { get; init; } = SearchFilter.None;

    /// <summary>
    /// Gets the order. <see cref="SortMode.Relevance"/> with neither text nor vector lists the newest imports first.
    /// </summary>
    public SortMode Sort { get; init; } = SortMode.Relevance;

    /// <summary>
    /// Gets whether to reverse the sort's natural direction: name A to Z, and newest, largest or most relevant first.
    /// </summary>
    public bool ReverseSort { get; init; }

    /// <summary>
    /// Gets the number of results to skip.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets the page size, from 1 to <see cref="MaxLimit"/>.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Gets the ranking settings.
    /// </summary>
    public SearchRankingOptions Ranking { get; init; } = SearchRankingOptions.Default;

    /// <summary>
    /// Gets whether to report which fields matched each hit, for highlighting. It costs one more query per page.
    /// </summary>
    public bool IncludeMatchedFields { get; init; } = true;

    /// <summary>
    /// Gets when a hit that only the vector search found counts as relevant (E09-S01-T03), or <c>null</c> to treat
    /// every hit as relevant. It has no effect without a <see cref="Vector"/>.
    /// </summary>
    public SemanticRelevanceCut? RelevanceCut { get; init; }

    /// <summary>
    /// Gets whether hits below <see cref="RelevanceCut"/> are returned, marked <see cref="SearchHit.IsLessRelevant"/>
    /// ("Show less relevant results"), instead of left out.
    /// </summary>
    public bool IncludeLessRelevant { get; init; }
}
