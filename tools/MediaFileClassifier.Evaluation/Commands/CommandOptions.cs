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
                _ => throw new ArgumentException($"Unknown option '{name}'."),
            };
        }

        return options;
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
