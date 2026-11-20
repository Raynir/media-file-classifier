#if DEBUG
using System.Globalization;

namespace MediaFileClassifier.Desktop.Smoke;

/// <summary>
/// The smoke tool's command line (MFC-1006): <c>--smoke &lt;script&gt; [--data-dir &lt;folder&gt;] [--out &lt;folder&gt;]</c>.
/// </summary>
/// <param name="ScriptPath">The script file.</param>
/// <param name="DataDirectory">The app-data folder for this run, used instead of the user's.</param>
/// <param name="OutputDirectory">Where screenshots and the report go.</param>
/// <param name="SampleMediaDirectory">The repository's <c>tests/SampleMedia</c>, or <see langword="null"/> when it isn't found.</param>
internal sealed record SmokeOptions(string ScriptPath, string DataDirectory, string OutputDirectory, string? SampleMediaDirectory)
{
    /// <summary>
    /// Gets the script's folder, which relative paths in the script start from.
    /// </summary>
    public string ScriptDirectory => Path.GetDirectoryName(ScriptPath)!;

    /// <summary>
    /// Reads the options, or returns <see langword="null"/> without <c>--smoke</c>.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The options, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">An option is missing its value, or the data folder isn't empty.</exception>
    public static SmokeOptions? FromArgs(string[] args)
    {
        if (Value(args, "--smoke") is not { } script)
        {
            return null;
        }

        script = Path.GetFullPath(script);
        var dataDirectory = Path.GetFullPath(Value(args, "--data-dir") ?? Path.Combine(
            Path.GetTempPath(),
            "MediaFileClassifier-smoke",
            $"{Path.GetFileNameWithoutExtension(script)}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}"));

        // A run never reuses app data: a fresh folder keeps runs repeatable, and nothing has to be deleted.
        if (Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory).Any())
        {
            throw new ArgumentException($"The data folder must be new or empty: {dataDirectory}");
        }

        var output = Path.GetFullPath(Value(args, "--out") ?? Path.Combine(dataDirectory, "shots"));
        return new SmokeOptions(script, dataDirectory, output, FindSampleMedia(Path.GetDirectoryName(script)!) ?? FindSampleMedia(AppContext.BaseDirectory));
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }

        return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : throw new ArgumentException($"{name} needs a value.");
    }

    private static string? FindSampleMedia(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "SampleMedia");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
#endif
