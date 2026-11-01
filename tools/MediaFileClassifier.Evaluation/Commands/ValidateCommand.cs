using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Checks a data set, and with <c>--files</c> also the media files and their checksums.
/// </summary>
internal static class ValidateCommand
{
    public static int Run(CommandOptions options, TextWriter output)
    {
        var data = EvaluateCommand.Load(options);
        var errors = DataValidator.Validate(data, options.CheckFiles);
        if (errors.Count > 0)
        {
            Print(errors, output);
            return 1;
        }

        var languages = string.Join(", ", data.Queries.GroupBy(q => q.Language).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}"));
        output.WriteLine($"{data.Info.Name}: {data.Items.Count} items, queries {languages}, {data.OcrTruth.Count} OCR images. Valid{(options.CheckFiles ? ", files match" : string.Empty)}.");
        return 0;
    }

    public static void Print(IReadOnlyList<string> errors, TextWriter output)
    {
        output.WriteLine($"{errors.Count} problems:");
        foreach (var error in errors)
        {
            output.WriteLine("  " + error);
        }
    }
}
