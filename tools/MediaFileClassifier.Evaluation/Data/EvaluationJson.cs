using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaFileClassifier.Evaluation.Data;

/// <summary>
/// The JSON settings for every file the runner reads and writes.
/// </summary>
internal static class EvaluationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    /// <summary>
    /// Writes a list with one entry per line, the format of the hand-edited data files.
    /// </summary>
    public static void WriteLines<T>(string path, IEnumerable<T> items)
    {
        var compact = new JsonSerializerOptions(Options) { WriteIndented = false };
        var lines = items.Select(i => "  " + JsonSerializer.Serialize(i, compact));
        File.WriteAllText(path, "[\n" + string.Join(",\n", lines) + "\n]\n");
    }
}
