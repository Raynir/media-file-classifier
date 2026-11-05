using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>
/// Checks the .NET SigLIP 2 text tower against the Python reference (E06-S03-T02). Needs the model files, so it
/// skips where they aren't installed, e.g. in CI.
/// </summary>
public sealed class Siglip2TextReferenceTests
{
    private const double MinCosine = 0.999;

    [Fact]
    public async Task EmbedTexts_EnglishAndRussianQueries_MatchReference()
    {
        using var model = CreateModel(batchSize: 1);
        var reference = Siglip2TestModel.Reference.Texts;

        var output = await model.EmbedTextsAsync([.. reference.Select(text => text.Text)], TestContext.Current.CancellationToken);

        Assert.Equal(Siglip2Model.PreprocessingVersion, output.PromptVersion);
        for (var i = 0; i < reference.Count; i++)
        {
            var cosine = Siglip2TestModel.Cosine(output.Value[i].Span, reference[i].Vector);
            Assert.True(cosine >= MinCosine, $"{reference[i].Id}: cosine {cosine:F5}");
        }
    }

    private static Siglip2OnnxModel CreateModel(int batchSize) =>
        new(Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory()), new OnnxSessionFactory(), ExecutionProvider.Cpu, batchSize);
}
