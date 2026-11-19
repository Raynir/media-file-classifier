using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

namespace MediaFileClassifier.Infrastructure.Tests.AI.OpenAiCompatible;

/// <summary>
/// Tests for <see cref="VisionModelDefaults"/> (E06-S04-T08): how many pictures a describer request carries per model.
/// </summary>
public sealed class VisionModelDefaultsTests
{
    [Theory]
    [InlineData("qwen/qwen3-vl-4b", 6)]
    [InlineData("Qwen3-VL-8B-Instruct", 6)]
    [InlineData("google/gemma-4-e2b", 6)]
    [InlineData("lmstudio-community/gemma-4-e4b", 6)]
    [InlineData("llava-v1.5-7b", 1)]
    [InlineData("google/gemma-3-4b", 1)]
    [InlineData("my-qwen3-vl", 1)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    public void GetMaxImages_GivesSeveralOnlyToTheKnownMultiImageFamilies(string? model, int expected) =>
        Assert.Equal(expected, VisionModelDefaults.GetMaxImages(model));

    [Theory]
    [InlineData("llava-v1.5-7b", 4, 4)]
    [InlineData("qwen/qwen3-vl-4b", 1, 1)]
    [InlineData("qwen/qwen3-vl-4b", 0, 6)]
    [InlineData("llava-v1.5-7b", -2, 1)]
    [InlineData("qwen/qwen3-vl-4b", null, 6)]
    public void Resolve_PrefersTheEndpointsOwnSetting_WhenItIsUsable(string model, int? configured, int expected) =>
        Assert.Equal(expected, VisionModelDefaults.Resolve(model, configured));

    [Fact]
    public void Create_GivesASingleImageDescriber_ForOnePicturePerRequest()
    {
        var endpoint = new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "llava");

        using var single = OpenAiCompatibleImageDescriber.Create("s", "S", endpoint, 1);
        using var multi = OpenAiCompatibleImageDescriber.Create("m", "M", endpoint, 4);

        Assert.IsNotAssignableFrom<Abstractions.AI.IMultiImageDescriber>(single);
        Assert.Equal(4, Assert.IsType<OpenAiCompatibleMultiImageDescriber>(multi).MaxImages);
    }
}
