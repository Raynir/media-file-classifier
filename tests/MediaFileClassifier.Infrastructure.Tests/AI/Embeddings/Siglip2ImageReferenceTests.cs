using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>
/// Checks the .NET SigLIP 2 image pipeline against the Python reference (E06-S03-T01). Needs the model files, so
/// it skips where they aren't installed, e.g. in CI.
/// </summary>
public sealed class Siglip2ImageReferenceTests
{
    // The tolerance the user chose: cosine similarity to the reference of at least 0.999 per sample.
    private const double MinCosine = 0.999;

    [Fact]
    public async Task EmbedImages_SampleMedia_MatchReference()
    {
        using var model = CreateModel(batchSize: 4);
        var reference = Siglip2TestModel.Reference.Images;
        var images = reference.Select(image => new AiImage(File.ReadAllBytes(Path.Combine(SampleMedia.Directory, image.File)), "image/*", image.File)).ToList();

        var output = await model.EmbedImagesAsync(images, TestContext.Current.CancellationToken);

        Assert.Equal(reference.Count, output.Value.Count);
        for (var i = 0; i < reference.Count; i++)
        {
            var cosine = Siglip2TestModel.Cosine(output.Value[i].Span, reference[i].Vector);
            Assert.True(cosine >= MinCosine, $"{reference[i].File}: cosine {cosine:F5}");
            TestContext.Current.SendDiagnosticMessage($"{reference[i].File}: cosine {cosine:F6}");
        }
    }

    [Fact]
    public async Task EmbedImages_SplitIntoBatches_SameAsOneAtATime()
    {
        using var batched = CreateModel(batchSize: 3);
        using var single = CreateModel(batchSize: 1);
        var images = Siglip2TestModel.Reference.Images.Take(5)
            .Select(image => new AiImage(File.ReadAllBytes(Path.Combine(SampleMedia.Directory, image.File)), "image/*", image.File))
            .ToList();

        var together = await batched.EmbedImagesAsync(images, TestContext.Current.CancellationToken);
        var apart = await single.EmbedImagesAsync(images, TestContext.Current.CancellationToken);

        for (var i = 0; i < images.Count; i++)
        {
            Assert.True(Siglip2TestModel.Cosine(together.Value[i].Span, apart.Value[i].ToArray()) > 0.99999);
        }
    }

    private static Siglip2OnnxModel CreateModel(int batchSize) =>
        new(Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory()), new OnnxSessionFactory(), ExecutionProvider.Cpu, batchSize);
}
