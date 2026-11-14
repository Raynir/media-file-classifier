namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// The quality thresholds a run is compared with: <c>baselines.json</c>. A missing or null
/// value means "not set yet"; E06-SP01 (MFC-53) records the first search values.
/// </summary>
public sealed record Baselines
{
    /// <summary>
    /// The minimum recall@10 per query language.
    /// </summary>
    public IReadOnlyDictionary<string, double?> RecallAt10 { get; init; } = new Dictionary<string, double?>();

    /// <summary>
    /// The minimum MRR per query language.
    /// </summary>
    public IReadOnlyDictionary<string, double?> Mrr { get; init; } = new Dictionary<string, double?>();

    /// <summary>
    /// The maximum share of the queries that should return nothing (category <c>none</c>) that still show a result,
    /// per query language.
    /// </summary>
    public IReadOnlyDictionary<string, double?> NoneShown { get; init; } = new Dictionary<string, double?>();

    /// <summary>
    /// The maximum OCR character error rate per script group.
    /// </summary>
    public IReadOnlyDictionary<string, double?> OcrCer { get; init; } = new Dictionary<string, double?>();

    /// <summary>
    /// The pinned baseline run of each system, by system name: the report file name (with or without <c>.json</c>)
    /// in the reports folder. Every run of that system shows its change from it (E09-S05).
    /// </summary>
    public IReadOnlyDictionary<string, string> References { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Who set the values and from which report, for the record.
    /// </summary>
    public string? Note { get; init; }
}
