namespace MediaFileClassifier.Infrastructure.Abstractions.Search;

/// <summary>
/// How results are ranked. Hybrid queries merge the keyword and vector lists with Reciprocal Rank Fusion: an item
/// scores <c>Σ weight / (k + rank)</c> over the lists it's in, with ranks from 1. The defaults come from the tuning
/// pass on the evaluation corpus (E09-S05, <c>docs/search-index.md</c>): k 10, both lists weight 1, all fields weight 1.
/// </summary>
public sealed record SearchRankingOptions
{
    /// <summary>
    /// Gets the default settings.
    /// </summary>
    public static SearchRankingOptions Default { get; } = new();

    /// <summary>
    /// Gets the RRF constant k. Larger values flatten the difference between top and lower ranks.
    /// </summary>
    public double RrfK { get; init; } = 10;

    /// <summary>
    /// Gets the weight of the keyword list in hybrid queries.
    /// </summary>
    public double KeywordWeight { get; init; } = 1.0;

    /// <summary>
    /// Gets the weight of the vector list in hybrid queries.
    /// </summary>
    public double VectorWeight { get; init; } = 1.0;

    /// <summary>
    /// Gets how many of the best results each list contributes to vector and hybrid queries. Results of those modes
    /// stop there; a keyword-only query pages through every match.
    /// </summary>
    public int CandidateLimit { get; init; } = 200;

    /// <summary>
    /// Gets the BM25 weights of the keyword fields. A match in a heavier field ranks higher.
    /// </summary>
    public KeywordFieldWeights FieldWeights { get; init; } = KeywordFieldWeights.Default;

    /// <summary>
    /// Checks the settings.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A setting is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RrfK);
        ArgumentOutOfRangeException.ThrowIfNegative(KeywordWeight);
        ArgumentOutOfRangeException.ThrowIfNegative(VectorWeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(CandidateLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(CandidateLimit, 10_000);
        ArgumentNullException.ThrowIfNull(FieldWeights);
        FieldWeights.Validate();
    }
}

/// <summary>
/// BM25 weights of the keyword index's fields.
/// </summary>
public sealed record KeywordFieldWeights
{
    /// <summary>
    /// Gets the default weights: 1 for every field. The tuning pass (E09-S05) found that boosting the title, tags or
    /// file name didn't raise recall on the evaluation corpus.
    /// </summary>
    public static KeywordFieldWeights Default { get; } = new();

    public double Title { get; init; } = 1;

    /// <summary>
    /// Gets the weight of the description and the summary.
    /// </summary>
    public double Description { get; init; } = 1;

    public double Tags { get; init; } = 1;

    public double OcrText { get; init; } = 1;

    public double Transcript { get; init; } = 1;

    /// <summary>
    /// Gets the weight of the notes and the list fields (objects, scenes, actions, emotions, people).
    /// </summary>
    public double Other { get; init; } = 1;

    /// <summary>
    /// Gets the weight of the item's path within its folder.
    /// </summary>
    public double FileName { get; init; } = 1;

    /// <summary>
    /// Checks the weights.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A weight is negative or not finite.</exception>
    public void Validate()
    {
        foreach (var weight in new[] { Title, Description, Tags, OcrText, Transcript, Other, FileName })
        {
            if (!double.IsFinite(weight) || weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Field weights must be finite and not negative.");
            }
        }
    }
}
