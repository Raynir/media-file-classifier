using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>
/// Finds the SigLIP 2 files for the tests that run the real model, and loads the Python reference outputs. The model
/// is about 1.5 GB and not in Git: put it in <c>models/siglip2</c> at the repository root (see
/// <c>tools/embedding-reference/README.md</c>) or point <c>MFC_SIGLIP2_DIR</c> at it. Without it those tests skip.
/// </summary>
internal static class Siglip2TestModel
{
    private static readonly Lazy<string?> DirectoryPath = new(FindDirectory);
    private static readonly Lazy<Siglip2Reference> LazyReference = new(LoadReference);

    public static string? Directory => DirectoryPath.Value;

    public static Siglip2Reference Reference => LazyReference.Value;

    public static string? TokenizerPath => Directory is { } directory ? Path.Combine(directory, "tokenizer.model") : null;

    public static string RequireDirectory()
    {
        Assert.SkipWhen(Directory is null, "The SigLIP 2 model isn't in models/siglip2 or MFC_SIGLIP2_DIR.");
        return Directory!;
    }

    public static InstalledModel CreateInstalled(string directory) => new(
        Siglip2Model.ModelId,
        Siglip2Model.Version,
        "standard",
        "SigLIP 2 base-patch16-256",
        "Apache-2.0",
        "test",
        directory,
        [
            new InstalledModelFile(Siglip2Model.VisionRole, "vision_model.onnx", 0, "0"),
            new InstalledModelFile(Siglip2Model.TextRole, "text_model.onnx", 0, "0"),
            new InstalledModelFile(Siglip2Model.TokenizerRole, "tokenizer.model", 0, "0"),
        ],
        "abc123",
        DateTimeOffset.UnixEpoch);

    public static double Cosine(ReadOnlySpan<float> a, IReadOnlyList<float> b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }

        return dot / Math.Sqrt(na * nb);
    }

    private static string? FindDirectory()
    {
        if (Environment.GetEnvironmentVariable("MFC_SIGLIP2_DIR") is { Length: > 0 } configured)
        {
            return HasFiles(configured) ? configured : null;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "models", "siglip2");
            if (HasFiles(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasFiles(string directory) =>
        File.Exists(Path.Combine(directory, "vision_model.onnx"))
        && File.Exists(Path.Combine(directory, "text_model.onnx"))
        && File.Exists(Path.Combine(directory, "tokenizer.model"));

    private static Siglip2Reference LoadReference()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "AI", "Embeddings", "siglip2-reference.json"));
        return JsonSerializer.Deserialize<Siglip2Reference>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The reference file is empty.");
    }
}

internal sealed record Siglip2Reference(
    IReadOnlyList<Siglip2ReferenceImage> Images,
    IReadOnlyList<Siglip2ReferenceText> Texts,
    IReadOnlyList<Siglip2ReferenceTokens> Tokens);

internal sealed record Siglip2ReferenceImage(string File, IReadOnlyList<float> Vector, string? ResizedSha256);

internal sealed record Siglip2ReferenceText(string Id, string Text, IReadOnlyList<float> Vector);

internal sealed record Siglip2ReferenceTokens(string Id, string Text, IReadOnlyList<long> Ids);
