using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaFileClassifier.UseCases.Analysis.Pipeline;

/// <summary>
/// The payload of an <see cref="AnalysisStages.Item"/> work item: which analyzers to run. Without one, every analyzer of
/// the library's profile runs. Stored as JSON with the work item, so the shape never changes.
/// </summary>
/// <param name="Analyzers">Analyzer keys, e.g. only <c>ocr</c> to retry OCR.</param>
public sealed record AnalysisPayload([property: JsonPropertyName("analyzers")] IReadOnlyList<string> Analyzers)
{
    /// <summary>
    /// Writes a payload, or none for every analyzer.
    /// </summary>
    /// <param name="analyzers">Analyzer keys, or <c>null</c> for every analyzer.</param>
    /// <returns>The JSON, or <c>null</c>.</returns>
    public static string? Serialize(IReadOnlyCollection<string>? analyzers) =>
        analyzers is null ? null : JsonSerializer.Serialize(new AnalysisPayload([.. analyzers.Distinct(StringComparer.Ordinal)]));

    /// <summary>
    /// Reads the analyzer keys from a payload.
    /// </summary>
    /// <param name="payload">The work item's payload.</param>
    /// <returns>The analyzer keys, or <c>null</c> for every analyzer.</returns>
    /// <exception cref="JsonException">The payload isn't an analysis payload.</exception>
    public static IReadOnlyList<string>? ReadAnalyzers(string? payload) =>
        string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<AnalysisPayload>(payload)?.Analyzers;
}
