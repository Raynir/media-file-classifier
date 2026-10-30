using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaFileClassifier.Benchmarks.Running;

internal static class BenchmarkJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}
