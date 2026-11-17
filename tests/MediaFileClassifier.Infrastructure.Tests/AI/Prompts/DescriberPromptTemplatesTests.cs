using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;
using MediaFileClassifier.Infrastructure.AI.Prompts;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Prompts;

/// <summary>
/// Tests for <see cref="DescriberPromptTemplates"/> (E06-S04-T04).
/// </summary>
public sealed class DescriberPromptTemplatesTests
{
    /// <summary>
    /// The content hash of every shipped template. A failure here means a template's text changed: give the change a
    /// new version (a new file and an entry in <see cref="DescriberPromptTemplates.Versions"/>) so analyzed items
    /// become outdated (D358), and add its hash here. Never change the hash of a shipped version.
    /// </summary>
    public static readonly TheoryData<string, string> PinnedHashes = new()
    {
        { "describe-1", "83dd51c26fc6f080ccb7f29dfbdc0fc9553afb557b455a2e28648f427f1548a5" },
    };

    [Theory]
    [MemberData(nameof(PinnedHashes))]
    public void ShippedTemplate_KeepsItsText(string version, string hash) =>
        Assert.Equal(hash, DescriberPromptTemplates.Resolve(version).ContentHash);

    [Fact]
    public void EveryShippedVersion_HasAPinnedHash_AndLoads()
    {
        Assert.Equal(PinnedHashes.Select(row => row.Data.Item1), DescriberPromptTemplates.Versions);
        Assert.Equal(DescriberPromptTemplates.Versions, DescriberPromptTemplates.All.Select(template => template.Version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("describe-unknown")]
    public void Resolve_NoneOrUnknown_IsTheLatest(string? requested) =>
        Assert.Same(DescriberPromptTemplates.Latest, DescriberPromptTemplates.Resolve(requested));

    [Fact]
    public void BuildRequest_FillsInTheLanguageLimitAndContext()
    {
        var template = DescriberPromptTemplates.Resolve("describe-1");

        var request = template.BuildRequest(new ImageDescriptionOptions("ru", "A frame of a video."));

        Assert.Equal(
            "Describe this image. Write all text in Russian. Fields: title, description, summary, tags, objects, scenes, actions, emotions. "
            + "Use JSON lists of short phrases for the last five, at most 30 each. Context: A frame of a video.",
            request);
    }

    [Fact]
    public void BuildRequest_WithoutContextOrLanguage_AsksForEnglish() =>
        Assert.EndsWith(
            "Write all text in English. Fields: title, description, summary, tags, objects, scenes, actions, emotions. Use JSON lists of short phrases for the last five, at most 30 each.",
            DescriberPromptTemplates.Resolve("describe-1").BuildRequest(ImageDescriptionOptions.Default));

    [Fact]
    public void Describer_UsesTheVersionTheProfileAsksFor_OrTheLatest()
    {
        using var describer = new OpenAiCompatibleImageDescriber("lmstudio:describe", "LM Studio", new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "model"));

        Assert.Equal(DescriberPromptTemplates.Latest.Version, describer.PromptVersion);
        Assert.Equal("describe-1", describer.GetPromptVersion("describe-1"));
        Assert.Equal(DescriberPromptTemplates.Latest.Version, describer.GetPromptVersion("describe-unknown"));
    }
}
