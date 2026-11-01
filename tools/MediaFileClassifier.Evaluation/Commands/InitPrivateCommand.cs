using System.Security.Cryptography;
using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Commands;

/// <summary>
/// Starts a private data set (E01-S10-T05): lists a folder of personal media into a manifest, and
/// creates empty query and OCR files to fill in. Nothing is copied; the media stay where they are.
/// </summary>
internal static class InitPrivateCommand
{
    private static readonly Dictionary<string, string> KindByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "photo", [".jpeg"] = "photo", [".png"] = "photo", [".webp"] = "photo", [".heic"] = "photo",
        [".gif"] = "gif",
        [".mp4"] = "video", [".mov"] = "video", [".mkv"] = "video", [".webm"] = "video", [".avi"] = "video",
    };

    public static int Run(CommandOptions options, TextWriter output)
    {
        if (options.Media is null)
        {
            throw new ArgumentException("init-private needs --media <folder> and --data <folder>.");
        }

        var manifest = Path.Combine(options.Data, EvaluationData.ManifestFile);
        if (File.Exists(manifest))
        {
            output.WriteLine($"{manifest} already exists; delete it to start over.");
            return 1;
        }

        WarnIfTracked(options.Data, output);
        var files = Directory.EnumerateFiles(options.Media, "*", SearchOption.AllDirectories)
            .Where(f => KindByExtension.ContainsKey(Path.GetExtension(f)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var items = files.Select((path, index) =>
        {
            using var stream = File.OpenRead(path);
            return new CorpusItem
            {
                Id = $"p-{index + 1:0000}",
                Kind = KindByExtension[Path.GetExtension(path)],
                File = Path.GetRelativePath(options.Media, path).Replace('\\', '/'),
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)),
            };
        });

        Directory.CreateDirectory(options.Data);
        EvaluationJson.WriteLines(manifest, items);
        File.WriteAllText(
            Path.Combine(options.Data, EvaluationData.DataSetFile),
            System.Text.Json.JsonSerializer.Serialize(new DataSetInfo { Name = "private", MediaRoot = options.Media, Private = true }, EvaluationJson.Options) + "\n");
        CreateIfMissing(Path.Combine(options.Data, EvaluationData.QueriesFile));
        CreateIfMissing(Path.Combine(options.Data, EvaluationData.OcrTruthFile));
        output.WriteLine($"Listed {files.Count} files in {manifest}. Add queries to {EvaluationData.QueriesFile}, then run evaluate with --data {options.Data}.");
        return 0;
    }

    private static void CreateIfMissing(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "[\n]\n");
        }
    }

    private static void WarnIfTracked(string data, TextWriter output)
    {
        for (var directory = new DirectoryInfo(data); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                var relative = Path.GetRelativePath(directory.FullName, data).Replace('\\', '/');
                if (!relative.StartsWith("eval/private", StringComparison.Ordinal))
                {
                    output.WriteLine($"Warning: {data} is inside a Git repository but not under eval/private/, so Git may track it.");
                }

                return;
            }
        }
    }
}
