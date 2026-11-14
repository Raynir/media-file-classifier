using System.Text.Json;

namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// The AI analysis of every corpus item (E09-S05): what the app's describer and OCR wrote for it, stored once in
/// <c>eval/analysis/&lt;model&gt;.json</c> so evaluations of keyword and hybrid search are deterministic.
/// </summary>
public sealed record CorpusAnalysis
{
    /// <summary>
    /// Gets the vision model on the OpenAI-compatible endpoint, e.g. <c>qwen/qwen3-vl-8b</c>.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Gets the describer's prompt version (<c>ImageDescriptionContract.Version</c>).
    /// </summary>
    public string? PromptVersion { get; init; }

    /// <summary>
    /// Gets the language the describer wrote in (the profile language).
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Gets the analysis of each item, by item ID.
    /// </summary>
    public SortedDictionary<string, ItemAnalysis> Items { get; init; } = new(StringComparer.Ordinal);

    public static CorpusAnalysis Load(string path) =>
        JsonSerializer.Deserialize<CorpusAnalysis>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options) + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonSerializerOptions Options => EvaluationJson.Options;
}

/// <summary>
/// What the describer and OCR wrote for one item. A field the model left empty is null.
/// </summary>
public sealed record ItemAnalysis
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Objects { get; init; } = [];

    public IReadOnlyList<string> Scenes { get; init; } = [];

    public IReadOnlyList<string> Actions { get; init; } = [];

    public IReadOnlyList<string> Emotions { get; init; } = [];

    /// <summary>
    /// Gets the recognized text; for a video or animated image, the distinct lines of every keyframe.
    /// </summary>
    public string? OcrText { get; init; }
}
