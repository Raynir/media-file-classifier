using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

/// <summary>
/// The minimal ONNX provider: features of the text go through the model and come out normalized.
/// </summary>
internal sealed class TinyTextEmbedder(string modelPath, int width)
    : OnnxModelProvider(
        "onnx:tiny",
        "Tiny",
        new ModelIdentity(ProviderIds.Onnx, "tiny", "1", embeddingDimension: width),
        new Dictionary<string, string> { ["text"] = modelPath },
        new OnnxSessionFactory(),
        ExecutionProvider.Cpu),
    ITextEmbeddingProvider
{
    public int Dimension => width;

    public ExecutionProvider LoadedExecutionProvider => GetSession("text").ExecutionProvider;

    public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        RunAsync<IReadOnlyList<ReadOnlyMemory<float>>>(() => [.. texts.Select(Embed)], cancellationToken);

    private ReadOnlyMemory<float> Embed(string text)
    {
        float[] features = [text.Length, text.Count(char.IsWhiteSpace), text.Count(char.IsLetter), 1];
        var input = new DenseTensor<float>(features, [1, width]);
        using var results = GetSession("text").Session.Run([NamedOnnxValue.CreateFromTensor(TinyOnnxModel.InputName, input)]);
        var output = results[0].AsEnumerable<float>().ToArray();
        var length = MathF.Sqrt(output.Sum(value => value * value));
        return output.Select(value => value / length).ToArray();
    }
}
