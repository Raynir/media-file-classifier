using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Describers;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Describers;

/// <summary>
/// The in-process Florence-2 describer (E06-S04-T01). The tests that run the real model need its files (about 1.1 GB
/// for base), which aren't in Git: point <c>MFC_FLORENCE2_DIR</c> at a folder with <c>tokenizer.json</c> and
/// <c>onnx/</c> as the onnx-community export has them, or put it in <c>models/florence2-base</c> at the repository
/// root (the MFC-105 spike's layout). Without it those tests skip.
/// </summary>
public sealed class Florence2DescriberTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ModelNotInstalled_IsModelMissing_AndDescribingThrowsModelMissing()
    {
        using var describer = new Florence2Describer(Florence2Model.Base, new InMemoryModelStore(), new OnnxSessionFactory(), new Florence2Options());

        var health = await describer.CheckHealthAsync(Token);
        var error = await Assert.ThrowsAsync<AiProviderException>(
            () => describer.DescribeAsync(new AiImage(new byte[1], "image/png"), ImageDescriptionOptions.Default, Token));

        Assert.Equal(ProviderHealthStatus.ModelMissing, health.Status);
        Assert.Equal(AiFailureKind.ModelMissing, error.Kind);
        Assert.Equal("florence2-base-ft", describer.Model.ModelName);
        Assert.Equal(AiProviderKind.Onnx, describer.Kind);
    }

    [Fact]
    public void Declares_DescriptionSummaryTagsAndObjects_AndItsOwnPromptVersion()
    {
        using var describer = new Florence2Describer(Florence2Model.Large, new InMemoryModelStore(), new OnnxSessionFactory(), new Florence2Options());

        Assert.Equal(DescriberFields.Description | DescriberFields.Summary | DescriberFields.Tags | DescriberFields.Objects, describer.SupportedFields);
        Assert.Equal("florence-1", describer.GetPromptVersion("describe-2"));
        Assert.Equal("florence-1", describer.PromptVersion);
        Assert.Equal("onnx:florence2-large-ft", describer.Id);
        Assert.IsNotAssignableFrom<IMultiImageDescriber>(describer);
    }

    [Fact]
    public void ObjectLabels_AreReadOncePerLabel_InOrder()
    {
        var answer = "<s>car<loc_1><loc_2><loc_3><loc_4>person<loc_5><loc_6><loc_7><loc_8>Car<loc_9><loc_10><loc_11><loc_12>wheel</s>";

        Assert.Equal(["car", "person"], Florence2OnnxModel.ObjectLabels(answer));
    }

    [Fact]
    public void BannedTokens_PreventARepeated3Gram()
    {
        // 7 8 9 … 7 8: a 9 now would repeat the 3-gram "7 8 9".
        Assert.Equal([9], Florence2OnnxModel.BannedTokens([2, 7, 8, 9, 5, 7, 8]));
        Assert.Empty(Florence2OnnxModel.BannedTokens([2, 7]));
    }

    [Fact]
    public void Tokenizer_EncodesTheTaskPrompts_LikeTheReference()
    {
        var tokenizer = Florence2Tokenizer.Load(Path.Combine(RequireModel(), "tokenizer.json"));

        // IDs from the Hugging Face tokenizers library (MFC-105 spike environment).
        Assert.Equal([0, 47066, 21700, 19, 10, 17818, 99, 16, 2343, 11, 5, 2274, 4, 2], tokenizer.Encode("Describe with a paragraph what is shown in the image."));
        Assert.Equal([0, 2264, 473, 5, 2274, 6190, 116, 2], tokenizer.Encode("What does the image describe?"));
        Assert.Equal([0, 574, 22486, 5, 8720, 19, 4120, 766, 11, 5, 2274, 4, 2], tokenizer.Encode("Locate the objects with category name in the image."));
        Assert.Equal([0, 31414, 6, 885, 17920, 4779, 1437, 1437, 22063, 3602, 25417, 19002, 155, 495, 328, 2], tokenizer.Encode("Hello, wörld  ёж 3D!"));
    }

    [Fact]
    public void Tokenizer_Decodes_WithOrWithoutTheAddedTokens()
    {
        var tokenizer = Florence2Tokenizer.Load(Path.Combine(RequireModel(), "tokenizer.json"));
        int[] ids = [574, 22486, 50269, 50300, 50400, 50500, 1437, 8720];

        Assert.Equal("Locate<loc_0><loc_31><loc_131><loc_231>  objects", tokenizer.Decode(ids, skipAdded: false));
        Assert.Equal("Locate  objects", tokenizer.Decode(ids, skipAdded: true));
        Assert.Equal("Hello, wörld  ёж 3D!", tokenizer.Decode(tokenizer.Encode("Hello, wörld  ёж 3D!").Select(id => (int)id), skipAdded: true));
    }

    [Fact]
    public async Task RealModel_DescribesAPhoto_WithTheDeclaredFieldsOnly()
    {
        var directory = RequireModel();
        var store = new InMemoryModelStore();
        store.Installed.Add(CreateInstalled(directory));
        using var describer = new Florence2Describer(Florence2Model.Base, store, new OnnxSessionFactory(), new Florence2Options());
        var image = new AiImage(await File.ReadAllBytesAsync(SampleMedia.JpegWithExif, Token), "image/jpeg", "jpeg-exif.jpg");

        var output = await describer.DescribeAsync(image, ImageDescriptionOptions.Default, Token);

        var description = output.Value;
        Assert.Equal("florence-1", output.PromptVersion);
        Assert.Equal("florence2-base-ft", output.Model.ModelName);
        Assert.True(description.Description.Length > 20, description.Description);
        Assert.NotEmpty(description.Summary);
        Assert.Empty(description.Title);
        Assert.Empty(description.Scenes);
        Assert.Empty(description.Actions);
        Assert.Empty(description.Emotions);
        Assert.All(description.Tags, tag => Assert.Equal(tag.ToLowerInvariant(), tag));
        Assert.DoesNotContain("<", description.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measures seconds per image over the spike's inputs (E06-S04-T01 Done when) and writes each answer next to the
    /// spike's. Opt-in: set <c>MFC_FLORENCE2_SAMPLES</c> to a folder of pictures, e.g. the spike's
    /// <c>models/describer-spike/inputs</c>.
    /// </summary>
    [Fact]
    public async Task Speed_OverTheSpikeInputs()
    {
        var samples = Environment.GetEnvironmentVariable("MFC_FLORENCE2_SAMPLES");
        Assert.SkipWhen(string.IsNullOrEmpty(samples), "Set MFC_FLORENCE2_SAMPLES to a folder of pictures to measure Florence-2.");
        var directory = RequireModel();
        var store = new InMemoryModelStore();
        store.Installed.Add(CreateInstalled(directory));
        using var describer = new Florence2Describer(Florence2Model.Base, store, new OnnxSessionFactory(), new Florence2Options());
        var files = Directory.GetFiles(samples!, "*.png").Order(StringComparer.Ordinal).ToList();
        var report = new List<string>();
        var seconds = new List<double>();

        _ = await describer.DescribeAsync(new AiImage(await File.ReadAllBytesAsync(files[0], Token), "image/png", "warm-up"), ImageDescriptionOptions.Default, Token);
        foreach (var file in files)
        {
            var image = new AiImage(await File.ReadAllBytesAsync(file, Token), "image/png", Path.GetFileName(file));
            var stopwatch = Stopwatch.StartNew();
            var output = await describer.DescribeAsync(image, ImageDescriptionOptions.Default, Token);
            seconds.Add(stopwatch.Elapsed.TotalSeconds);
            report.Add($"{Path.GetFileNameWithoutExtension(file)}\t{stopwatch.Elapsed.TotalSeconds:F2}\t{output.Value.Summary}\t{string.Join(", ", output.Value.Objects)}\t{output.Value.Description}");
        }

        seconds.Sort();
        report.Insert(0, $"median {seconds[seconds.Count / 2]:F2} s, mean {seconds.Average():F2} s, n {seconds.Count}");
        await File.WriteAllLinesAsync(Path.Combine(Path.GetTempPath(), "mfc-florence2-speed.tsv"), report, Token);
    }

    private static string RequireModel()
    {
        var directory = FindModel();
        Assert.SkipWhen(directory is null, "Florence-2 base isn't in models/florence2-base or MFC_FLORENCE2_DIR.");
        return directory!;
    }

    private static string? FindModel()
    {
        if (Environment.GetEnvironmentVariable("MFC_FLORENCE2_DIR") is { Length: > 0 } configured)
        {
            return File.Exists(Path.Combine(configured, "tokenizer.json")) ? configured : null;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "models", "florence2-base");
            if (File.Exists(Path.Combine(candidate, "tokenizer.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static InstalledModel CreateInstalled(string directory) => new(
        Florence2Model.Base.ModelId,
        Florence2Model.Version,
        "standard",
        Florence2Model.Base.DisplayName,
        "MIT",
        "test",
        directory,
        [
            new InstalledModelFile(Florence2Model.VisionRole, Path.Combine("onnx", "vision_encoder.onnx"), 0, "0"),
            new InstalledModelFile(Florence2Model.EmbedRole, Path.Combine("onnx", "embed_tokens.onnx"), 0, "0"),
            new InstalledModelFile(Florence2Model.EncoderRole, Path.Combine("onnx", "encoder_model.onnx"), 0, "0"),
            new InstalledModelFile(Florence2Model.DecoderRole, Path.Combine("onnx", "decoder_model_merged.onnx"), 0, "0"),
            new InstalledModelFile(Florence2Model.TokenizerRole, "tokenizer.json", 0, "0"),
        ],
        "abc123",
        DateTimeOffset.UnixEpoch);
}
