namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// The options shared by the commands.
/// </summary>
internal sealed record CommandOptions
{
    /// <summary>
    /// The data folder: <c>eval</c> for the public set, or a private folder.
    /// </summary>
    public string Data { get; init; } = Path.GetFullPath("eval");

    /// <summary>
    /// True when <c>--data</c> was given, rather than defaulting to the public set.
    /// </summary>
    public bool DataGiven { get; init; }

    public string? Results { get; init; }

    public string? Out { get; init; }

    public string? System { get; init; }

    public string? Models { get; init; }

    public string? Media { get; init; }

    public bool CheckFiles { get; init; }

    public bool FailBelowBaseline { get; init; }

    /// <summary>
    /// The OpenAI-compatible endpoint for <c>analyze</c>.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// The vision model on the endpoint, for <c>analyze</c>, or whose stored analysis the keyword and hybrid systems read.
    /// </summary>
    public string? Vlm { get; init; }

    /// <summary>
    /// The stored analysis file, instead of <c>&lt;data&gt;/analysis/&lt;vlm&gt;.json</c>.
    /// </summary>
    public string? Analysis { get; init; }

    /// <summary>
    /// The language the describer writes in (the profile language).
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// The RRF constant k for the app's search, instead of the default.
    /// </summary>
    public double? RrfK { get; init; }

    public double? KeywordWeight { get; init; }

    public double? VectorWeight { get; init; }

    /// <summary>
    /// BM25 field weights, e.g. <c>title=4,tags=3,filename=2</c>; fields not named keep their defaults.
    /// </summary>
    public string? FieldWeights { get; init; }

    /// <summary>
    /// The relevance cut as <c>centred,raw</c>, or <c>none</c>.
    /// </summary>
    public string? Threshold { get; init; }

    /// <summary>
    /// ocr: the execution provider for an in-process engine, <c>cpu</c> or <c>directml</c>.
    /// </summary>
    public string? ExecutionProvider { get; init; }

    /// <summary>
    /// ocr: the longest side the in-process detector sees, if not its default.
    /// </summary>
    public int? DetectionMaxSide { get; init; }

    public static CommandOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CommandOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            options = name switch
            {
                "--data" => options with { Data = Path.GetFullPath(Value(args, ref i, name)), DataGiven = true },
                "--results" => options with { Results = Path.GetFullPath(Value(args, ref i, name)) },
                "--system" => options with { System = Value(args, ref i, name) },
                "--models" => options with { Models = Path.GetFullPath(Value(args, ref i, name)) },
                "--out" => options with { Out = Path.GetFullPath(Value(args, ref i, name)) },
                "--media" => options with { Media = Path.GetFullPath(Value(args, ref i, name)) },
                "--files" => options with { CheckFiles = true },
                "--fail-below-baseline" => options with { FailBelowBaseline = true },
                "--endpoint" => options with { Endpoint = Value(args, ref i, name) },
                "--vlm" => options with { Vlm = Value(args, ref i, name) },
                "--analysis" => options with { Analysis = Path.GetFullPath(Value(args, ref i, name)) },
                "--language" => options with { Language = Value(args, ref i, name) },
                "--rrf-k" => options with { RrfK = Number(args, ref i, name) },
                "--keyword-weight" => options with { KeywordWeight = Number(args, ref i, name) },
                "--vector-weight" => options with { VectorWeight = Number(args, ref i, name) },
                "--field-weights" => options with { FieldWeights = Value(args, ref i, name) },
                "--threshold" => options with { Threshold = Value(args, ref i, name) },
                "--det-max-side" => options with { DetectionMaxSide = (int)Number(args, ref i, name) },
                "--ep" => options with { ExecutionProvider = Value(args, ref i, name) },
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
        }

        return options;
    }

    private static double Number(IReadOnlyList<string> args, ref int i, string name)
    {
        var text = Value(args, ref i, name);
        return double.TryParse(text, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"Option '{name}' needs a number, not '{text}'.");
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name)
    {
        if (i + 1 >= args.Count)
        {
            throw new ArgumentException($"Option '{name}' needs a value.");
        }

        return args[++i];
    }
}
