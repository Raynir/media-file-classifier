using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// Results computed outside the runner, read from a JSON file:
/// <code>
/// {
///   "system": "siglip2-base-patch16-256",
///   "search": { "en-001": ["photo-001", "gif-002"] },
///   "ocr": { "synth-001": "Emma\nAre we still on for dinner tonight?" }
/// }
/// </code>
/// Both sections are optional. A query or image missing from the file scores as an empty result.
/// </summary>
public sealed class ResultsFile : IRetrievalSystem, IOcrEngine
{
    private readonly Contents contents;

    public ResultsFile(Contents contents)
    {
        this.contents = contents;
    }

    public string Name => contents.System;

    public bool HasSearch => contents.Search is not null;

    public bool HasOcr => contents.Ocr is not null;

    public static ResultsFile Load(string path) => new(EvaluationJson.Read<Contents>(path));

    public Task IndexAsync(IReadOnlyList<CorpusItem> items, string mediaRoot, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<string>> SearchAsync(EvalQuery query, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ranked = contents.Search?.GetValueOrDefault(query.Id) ?? [];
        return Task.FromResult<IReadOnlyList<string>>([.. ranked.Take(limit)]);
    }

    public Task<string?> RecognizeAsync(CorpusItem item, string path, CancellationToken cancellationToken) =>
        Task.FromResult(contents.Ocr?.GetValueOrDefault(item.Id));

    /// <summary>
    /// The file format.
    /// </summary>
    public sealed record Contents
    {
        public required string System { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>>? Search { get; init; }

        public IReadOnlyDictionary<string, string>? Ocr { get; init; }
    }
}
