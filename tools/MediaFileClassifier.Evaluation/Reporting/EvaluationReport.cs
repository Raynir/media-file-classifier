namespace MediaFileClassifier.Evaluation.Reporting;

/// <summary>
/// The result of one run, saved as JSON next to its Markdown rendering. Every metric is rounded
/// to four decimals, so two runs on the same input give byte-identical metric sections.
/// </summary>
public sealed record EvaluationReport
{
    public int Schema { get; init; } = 1;

    public required string System { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DataSummary Data { get; init; }

    public SearchSection? Search { get; init; }

    public OcrSection? Ocr { get; init; }

    /// <summary>
    /// The report this run is compared with, by file name; null for the first run.
    /// </summary>
    public string? Previous { get; init; }

    /// <summary>
    /// True when the previous run used different labels, so its deltas compare different questions.
    /// </summary>
    public bool? PreviousUsedOtherInputs { get; init; }

    public IReadOnlyList<MetricComparison> Comparisons { get; init; } = [];
}

public sealed record DataSummary(
    string Name,
    int Items,
    IReadOnlyDictionary<string, int> QueriesPerLanguage,
    int OcrImages,
    IReadOnlyDictionary<string, string> InputHashes);

public sealed record SearchSection(
    int Limit,
    IReadOnlyList<LanguageScore> Languages,
    IReadOnlyList<CategoryScore> Categories,
    IReadOnlyList<QueryScore> Queries);

public sealed record LanguageScore(string Language, int Queries, double RecallAt10, double Mrr);

public sealed record CategoryScore(string Language, string Category, int Queries, double RecallAt10, double Mrr);

public sealed record QueryScore(
    string Id,
    string Language,
    string Category,
    int Relevant,
    int Returned,
    int? FirstRelevantRank,
    double RecallAt10,
    double ReciprocalRank,
    IReadOnlyList<string> UnknownIds);

public sealed record OcrSection(IReadOnlyList<ScriptScore> Scripts, IReadOnlyList<ImageScore> Images);

/// <summary>
/// CER per script group, summed over images (micro average): total edits over total reference characters.
/// </summary>
public sealed record ScriptScore(string Script, int Images, int ReferenceCharacters, int Edits, double Cer);

public sealed record ImageScore(string Id, string Script, string Origin, int ReferenceCharacters, int Edits, double Cer, bool Missing);

/// <summary>
/// One headline metric against the previous run and the baseline threshold.
/// </summary>
/// <param name="Metric"><c>recall@10</c>, <c>mrr</c> or <c>ocr-cer</c>.</param>
/// <param name="Group">The language for search metrics, the script group for OCR.</param>
/// <param name="Value">This run's value.</param>
/// <param name="Previous">The previous run's value, if there was one.</param>
/// <param name="Delta">Value minus Previous.</param>
/// <param name="Threshold">The baseline, if set: a minimum for search, a maximum for OCR.</param>
/// <param name="Status"><c>pass</c>, <c>below</c> (worse than the baseline) or <c>no baseline</c>.</param>
public sealed record MetricComparison(
    string Metric,
    string Group,
    double Value,
    double? Previous,
    double? Delta,
    double? Threshold,
    string Status);
