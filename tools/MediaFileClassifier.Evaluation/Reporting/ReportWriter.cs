using System.Globalization;
using System.Text;

namespace MediaFileClassifier.Evaluation.Reporting;

/// <summary>
/// Renders a report as Markdown.
/// </summary>
public static class ReportWriter
{
    private const int WorstQueries = 15;

    public static string Write(EvaluationReport report)
    {
        var text = new StringBuilder();
        Line(text, $"# Evaluation report: {report.System}");
        text.Append('\n');
        Line(text, $"- Run: {report.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        var queries = string.Join(", ", report.Data.QueriesPerLanguage.Select(q => $"{q.Value} {q.Key}"));
        Line(text, $"- Data: {report.Data.Name}, {report.Data.Items} items, queries {queries}, {report.Data.OcrImages} OCR images");
        Line(text, $"- Inputs: {string.Join(", ", report.Data.InputHashes.Select(h => $"{h.Key} {h.Value[..Math.Min(12, h.Value.Length)]}"))}");
        Line(text, report.Previous is null
            ? $"- Previous run: none, so there are no deltas."
            : $"- Previous run: {report.Previous}{(report.PreviousUsedOtherInputs == true ? " (different labels, so deltas compare different questions)" : string.Empty)}");
        text.Append('\n');

        Line(text, $"## Summary");
        text.Append('\n');
        Line(text, "| Metric | Group | Value | Change | Baseline | Status |");
        Line(text, "|---|---|--:|--:|--:|---|");
        foreach (var c in report.Comparisons)
        {
            Line(text, $"| {c.Metric} | {c.Group} | {Number(c.Value)} | {Signed(c.Delta)} | {(c.Threshold is null ? "-" : Number(c.Threshold.Value))} | {c.Status} |");
        }

        text.Append('\n');
        if (report.Search is { } search)
        {
            Line(text, $"## Search by category");
            text.Append('\n');
            Line(text, $"Recall@10 and MRR over the first {search.Limit} results.");
            text.Append('\n');
            Line(text, "| Category | Language | Queries | Recall@10 | MRR |");
            Line(text, "|---|---|--:|--:|--:|");
            foreach (var c in search.Categories.Where(c => c.NoneShown is null))
            {
                Line(text, $"| {c.Category} | {c.Language} | {c.Queries} | {Number(c.RecallAt10)} | {Number(c.Mrr)} |");
            }

            var none = search.Queries.Where(q => q.Category == Data.QueryCategories.None).ToList();
            if (none.Count > 0)
            {
                text.Append('\n');
                Line(text, $"## Queries that should return nothing");
                text.Append('\n');
                Line(text, $"{none.Count(q => q.Returned > 0)} of {none.Count} showed results.");
                text.Append('\n');
                Line(text, "| Query | Results |");
                Line(text, "|---|--:|");
                foreach (var q in none.Where(q => q.Returned > 0).OrderByDescending(q => q.Returned).ThenBy(q => q.Id, StringComparer.Ordinal))
                {
                    Line(text, $"| {q.Id} | {q.Returned} |");
                }
            }

            text.Append('\n');
            Line(text, $"## Weakest queries");
            text.Append('\n');
            Line(text, "| Query | Category | Relevant | First relevant at | Recall@10 |");
            Line(text, "|---|---|--:|--:|--:|");
            foreach (var q in search.Queries.Where(q => q.Category != Data.QueryCategories.None).OrderBy(q => q.RecallAt10).ThenBy(q => q.ReciprocalRank).ThenBy(q => q.Id, StringComparer.Ordinal).Take(WorstQueries))
            {
                Line(text, $"| {q.Id} | {q.Category} | {q.Relevant} | {q.FirstRelevantRank?.ToString(CultureInfo.InvariantCulture) ?? "-"} | {Number(q.RecallAt10)} |");
            }

            var unknown = search.Queries.SelectMany(q => q.UnknownIds).Distinct(StringComparer.Ordinal).Count();
            var empty = search.Queries.Count(q => q.Returned == 0 && q.Category != Data.QueryCategories.None);
            if (unknown > 0 || empty > 0)
            {
                text.Append('\n');
                Line(text, $"Warnings: {empty} queries returned nothing; {unknown} returned IDs are not in the manifest.");
            }

            text.Append('\n');
        }

        if (report.Ocr is { } ocr)
        {
            Line(text, $"## OCR");
            text.Append('\n');
            Line(text, "| Script | Images | Reference characters | Edits | CER |");
            Line(text, "|---|--:|--:|--:|--:|");
            foreach (var s in ocr.Scripts)
            {
                Line(text, $"| {s.Script} | {s.Images} | {s.ReferenceCharacters} | {s.Edits} | {Number(s.Cer)} |");
            }

            var missing = ocr.Images.Count(i => i.Missing);
            if (missing > 0)
            {
                text.Append('\n');
                Line(text, $"Warning: no OCR output for {missing} images; they count as fully wrong.");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static string Number(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Signed(double? value) => value switch
    {
        null => "-",
        > 0 => "+" + Number(value.Value),
        _ => Number(value.Value),
    };

    private static void Line(StringBuilder text, FormattableString line) =>
        text.Append(line.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
