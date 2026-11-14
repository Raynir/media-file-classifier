using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// Checks that a data set is consistent: every label points at a real item, and the public set meets
/// the E01-S10 minimums and licence rules. It needs no media files unless asked to check them.
/// </summary>
public static partial class DataValidator
{
    public const int MinimumQueriesPerLanguage = 50;
    public const int MinimumOcrImages = 30;
    public const int MinimumPublicItems = 300;

    public static IReadOnlyList<string> Languages { get; } = ["en", "ru"];

    public static IReadOnlyList<string> Kinds { get; } = ["photo", "screenshot", "document", "gif", "video"];

    public static IReadOnlyList<string> Scripts { get; } = ["latin", "cyrillic"];

    /// <summary>
    /// Returns the problems found; an empty list means the data set is valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(EvaluationData data, bool checkFiles)
    {
        var errors = new List<string>();
        var items = CheckItems(data, errors);
        CheckQueries(data, items, errors);
        CheckOcrTruth(data, items, errors);
        if (checkFiles)
        {
            CheckFiles(data, errors);
        }

        return errors;
    }

    private static Dictionary<string, CorpusItem> CheckItems(EvaluationData data, List<string> errors)
    {
        var items = new Dictionary<string, CorpusItem>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in data.Items)
        {
            if (!items.TryAdd(item.Id, item))
            {
                errors.Add($"manifest: duplicate id {item.Id}");
            }

            if (!files.Add(item.File))
            {
                errors.Add($"manifest: {item.Id} reuses file {item.File}");
            }

            if (!Kinds.Contains(item.Kind))
            {
                errors.Add($"manifest: {item.Id} has unknown kind '{item.Kind}'");
            }

            if (!data.Info.Private)
            {
                CheckLicence(item, errors);
            }
        }

        if (!data.Info.Private && items.Count < MinimumPublicItems)
        {
            errors.Add($"manifest: {items.Count} items, the public set needs at least {MinimumPublicItems}");
        }

        return items;
    }

    private static void CheckLicence(CorpusItem item, List<string> errors)
    {
        var source = item.Source;
        if (source is null || (source.CommonsTitle is null && source.Synthetic is null))
        {
            errors.Add($"manifest: {item.Id} has no source");
            return;
        }

        if (source.Licence is null || !AllowedLicence().IsMatch(source.Licence))
        {
            errors.Add($"manifest: {item.Id} has licence '{source.Licence}', which the public set doesn't accept");
        }

        if (source.CommonsTitle is not null && (string.IsNullOrWhiteSpace(source.Author) || string.IsNullOrWhiteSpace(source.PageUrl)))
        {
            errors.Add($"manifest: {item.Id} lacks the author or source page needed for attribution");
        }

        if (item.Sha256 is null)
        {
            errors.Add($"manifest: {item.Id} has no checksum");
        }
    }

    private static void CheckQueries(EvaluationData data, Dictionary<string, CorpusItem> items, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in data.Queries)
        {
            if (!ids.Add(query.Id))
            {
                errors.Add($"queries: duplicate id {query.Id}");
            }

            if (!Languages.Contains(query.Language))
            {
                errors.Add($"queries: {query.Id} has unknown language '{query.Language}'");
            }

            if (!QueryCategories.All.Contains(query.Category))
            {
                errors.Add($"queries: {query.Id} has unknown category '{query.Category}'");
            }

            if (string.IsNullOrWhiteSpace(query.Text))
            {
                errors.Add($"queries: {query.Id} has no text");
            }

            if (query.Category == QueryCategories.None)
            {
                if (query.Relevant.Count > 0)
                {
                    errors.Add($"queries: {query.Id} should return nothing, so it can't list relevant items");
                }
            }
            else if (query.Relevant.Count == 0)
            {
                errors.Add($"queries: {query.Id} has no relevant items");
            }

            if (query.Relevant.Distinct(StringComparer.Ordinal).Count() != query.Relevant.Count)
            {
                errors.Add($"queries: {query.Id} lists a relevant item twice");
            }

            errors.AddRange(query.Relevant.Where(id => !items.ContainsKey(id)).Select(id => $"queries: {query.Id} points at unknown item {id}"));
        }

        if (!data.Info.Private)
        {
            foreach (var language in Languages)
            {
                var count = data.Queries.Count(q => q.Language == language);
                if (count < MinimumQueriesPerLanguage)
                {
                    errors.Add($"queries: {count} in '{language}', the public set needs at least {MinimumQueriesPerLanguage}");
                }
            }
        }
    }

    private static void CheckOcrTruth(EvaluationData data, Dictionary<string, CorpusItem> items, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.OcrTruth)
        {
            if (!ids.Add(entry.Id))
            {
                errors.Add($"ocr-truth: duplicate id {entry.Id}");
            }

            if (!items.TryGetValue(entry.Id, out var item))
            {
                errors.Add($"ocr-truth: unknown item {entry.Id}");
            }
            else if (item.Kind is "gif" or "video")
            {
                errors.Add($"ocr-truth: {entry.Id} is a {item.Kind}; OCR ground truth is for still images");
            }

            if (entry.Scripts.Count == 0 || entry.Scripts.Any(s => !Scripts.Contains(s)))
            {
                errors.Add($"ocr-truth: {entry.Id} has unknown scripts '{string.Join(",", entry.Scripts)}'");
            }

            if (entry.Origin is not ("synthetic" or "transcribed"))
            {
                errors.Add($"ocr-truth: {entry.Id} has unknown origin '{entry.Origin}'");
            }

            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                errors.Add($"ocr-truth: {entry.Id} has no text");
            }
        }

        if (!data.Info.Private)
        {
            if (data.OcrTruth.Count < MinimumOcrImages)
            {
                errors.Add($"ocr-truth: {data.OcrTruth.Count} images, the public set needs at least {MinimumOcrImages}");
            }

            errors.AddRange(Scripts
                .Where(script => !data.OcrTruth.Any(e => e.Scripts.Contains(script)))
                .Select(script => $"ocr-truth: no image with {script} text"));
        }
    }

    private static void CheckFiles(EvaluationData data, List<string> errors)
    {
        foreach (var item in data.Items)
        {
            var path = Path.Combine(data.MediaRoot, item.File);
            if (!File.Exists(path))
            {
                errors.Add($"files: {item.File} is missing from {data.MediaRoot}");
                continue;
            }

            if (item.Sha256 is not null)
            {
                using var stream = File.OpenRead(path);
                var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"files: {item.File} doesn't match its checksum");
                }
            }
        }
    }

    [GeneratedRegex(@"^(CC0( 1\.0)?|Public domain|CC BY(-SA)? [1-4]\.0( [a-z-]+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedLicence();
}
