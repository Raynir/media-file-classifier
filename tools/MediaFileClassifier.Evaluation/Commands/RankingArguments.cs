using System.Globalization;
using MediaFileClassifier.Evaluation.Systems;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Turns the ranking options of the command line into settings for the app's search (E09-S05). Anything not given
/// keeps the app's default.
/// </summary>
internal static class RankingArguments
{
    public static AppSearchSettings Parse(CommandOptions options) => Apply(AppSearchSettings.Default, options);

    public static AppSearchSettings Apply(AppSearchSettings settings, CommandOptions options)
    {
        var ranking = settings.Ranking with
        {
            RrfK = options.RrfK ?? settings.Ranking.RrfK,
            KeywordWeight = options.KeywordWeight ?? settings.Ranking.KeywordWeight,
            VectorWeight = options.VectorWeight ?? settings.Ranking.VectorWeight,
            FieldWeights = options.FieldWeights is null ? settings.Ranking.FieldWeights : ParseFieldWeights(options.FieldWeights, settings.Ranking.FieldWeights),
        };
        ranking.Validate();
        var cut = options.Threshold is null ? settings.Cut : ParseThreshold(options.Threshold);
        cut?.Validate();
        return new AppSearchSettings(ranking, cut);
    }

    /// <summary>
    /// Reads <c>field=weight</c> pairs separated by commas over the given weights.
    /// </summary>
    /// <param name="text">The pairs, e.g. <c>title=4,tags=3</c>.</param>
    /// <param name="weights">The weights the pairs change.</param>
    /// <returns>The new weights.</returns>
    public static KeywordFieldWeights ParseFieldWeights(string text, KeywordFieldWeights weights)
    {
        foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            {
                throw new ArgumentException($"--field-weights needs field=weight pairs, not '{pair}'.");
            }

            weights = parts[0].ToLowerInvariant() switch
            {
                "title" => weights with { Title = weight },
                "description" => weights with { Description = weight },
                "tags" => weights with { Tags = weight },
                "ocr" => weights with { OcrText = weight },
                "transcript" => weights with { Transcript = weight },
                "other" => weights with { Other = weight },
                "filename" => weights with { FileName = weight },
                _ => throw new ArgumentException($"Unknown field '{parts[0]}' in --field-weights; use title, description, tags, ocr, transcript, other or filename."),
            };
        }

        return weights;
    }

    /// <summary>
    /// Reads a relevance cut: <c>centred,raw</c> or <c>none</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The cut, or <c>null</c> for none.</returns>
    public static SemanticRelevanceCut? ParseThreshold(string text)
    {
        if (string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var centred)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
        {
            throw new ArgumentException($"--threshold needs 'centred,raw' (e.g. 0.07,0.13) or 'none', not '{text}'.");
        }

        return new SemanticRelevanceCut(centred, raw);
    }
}
