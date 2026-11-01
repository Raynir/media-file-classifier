using System.Security.Cryptography;
using System.Text;

namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// A loaded data folder: the manifest, the queries, the OCR ground truth and the baselines.
/// </summary>
public sealed record EvaluationData
{
    public const string ManifestFile = "manifest.json";
    public const string QueriesFile = "queries.json";
    public const string OcrTruthFile = "ocr-truth.json";
    public const string BaselinesFile = "baselines.json";
    public const string DataSetFile = "dataset.json";

    public required string Directory { get; init; }

    public required DataSetInfo Info { get; init; }

    public required IReadOnlyList<CorpusItem> Items { get; init; }

    public IReadOnlyList<EvalQuery> Queries { get; init; } = [];

    public IReadOnlyList<OcrTruthEntry> OcrTruth { get; init; } = [];

    public Baselines Baselines { get; init; } = new();

    /// <summary>
    /// SHA-256 of each input file, so a report shows whether the labels changed between runs.
    /// Line endings are normalized first, because Git checks text files out with CRLF on Windows.
    /// </summary>
    public IReadOnlyDictionary<string, string> InputHashes { get; init; } = new Dictionary<string, string>();

    public string MediaRoot => Path.GetFullPath(Path.Combine(Directory, Info.MediaRoot));

    public static EvaluationData Load(string directory)
    {
        var root = Path.GetFullPath(directory);
        var manifest = Path.Combine(root, ManifestFile);
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException($"No {ManifestFile} in {root}.", manifest);
        }

        // Without it a private set would be checked as the public one, with misleading licence and size errors.
        var info = Path.Combine(root, DataSetFile);
        if (!File.Exists(info))
        {
            throw new FileNotFoundException(
                $"No {DataSetFile} in {root}. A private set needs one, e.g. {{ \"name\": \"home\", \"mediaRoot\": \"D:\\\\Photos\", \"private\": true }}; init-private writes it.",
                info);
        }

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { ManifestFile, QueriesFile, OcrTruthFile, BaselinesFile })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
                hashes[name] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            }
        }

        return new EvaluationData
        {
            Directory = root,
            Info = EvaluationJson.Read<DataSetInfo>(info),
            Items = EvaluationJson.Read<List<CorpusItem>>(manifest),
            Queries = ReadOptional(root, QueriesFile, new List<EvalQuery>()),
            OcrTruth = ReadOptional(root, OcrTruthFile, new List<OcrTruthEntry>()),
            Baselines = ReadOptional(root, BaselinesFile, new Baselines()),
            InputHashes = hashes,
        };
    }

    private static T ReadOptional<T>(string root, string name, T fallback)
    {
        var path = Path.Combine(root, name);
        return File.Exists(path) ? EvaluationJson.Read<T>(path) : fallback;
    }
}
