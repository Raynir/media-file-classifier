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
        { "describe-2", "1428f164f94ef03c47464764410e1ca4eb38c6a6b319881ac863dd341e326cf8" },
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
    public void Templates_NameTheirSchema()
    {
        Assert.Equal(1, DescriberPromptTemplates.Resolve("describe-1").Schema);
        Assert.Equal(2, DescriberPromptTemplates.Resolve("describe-2").Schema);
    }

    [Fact]
    public void Describe2_SingleImage_IsTheSpikesPromptC()
    {
        // tools/describer-spike/prompt.py USER_C, as the spike scored it (MFC-105), with English as the language.
        const string PromptC = "Describe this image. A frame of a video. Write every field in English, except the fields that end in _ru, which are in Russian.\n\n"
            + "- title: a short name for the image, 3 to 8 words, no full stop. title_ru: the same title in natural Russian.\n"
            + "- description: 2 to 4 sentences: the main subject, the setting, notable details, and any readable text quoted exactly.\n"
            + "- summary: one sentence.\n"
            + "- tags: at most 15 keywords someone would type to find this image, most important first. Each has \"en\" and \"ru\": a lower-case noun or short noun phrase in the singular, the Russian one a natural Russian word in the nominative case (not a transliteration). No generic words like \"image\", \"photo\" or \"picture\", and no near-duplicates.\n"
            + "- objects: the visible things. scenes: the place or setting. actions: what is happening. emotions: the mood. Use JSON lists of short English phrases, at most 30 each.";
        var template = DescriberPromptTemplates.Resolve("describe-2");

        Assert.Equal(PromptC, template.BuildRequest(new ImageDescriptionOptions(Context: "A frame of a video.")));
        Assert.StartsWith("Describe this image. Write every field in English", template.BuildRequest(ImageDescriptionOptions.Default), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe2_Clip_ReplacesTheOpening_AndKeepsTheContext()
    {
        var request = DescriberPromptTemplates.Resolve("describe-2").BuildRequest(new ImageDescriptionOptions("ru", "Folder: {language} trip") { FrameCount = 4 });

        Assert.StartsWith(
            "The 4 pictures are frames of one video or animation, in time order. Describe the whole clip, not each frame. Folder: {language} trip Write every field in Russian,",
            request,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Describe1_Clip_PutsTheClipSentenceBeforeTheContext()
    {
        var request = DescriberPromptTemplates.Resolve("describe-1").BuildRequest(new ImageDescriptionOptions(Context: "Folder: Trip") { FrameCount = 3 });

        Assert.StartsWith("Describe this image. Write all text in English.", request, StringComparison.Ordinal);
        Assert.EndsWith(
            "Context: The 3 pictures are frames of one video or animation, in time order. Describe the whole clip, not each frame. Folder: Trip",
            request,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Describer_UsesTheVersionTheProfileAsksFor_OrTheLatest()
    {
        using var describer = new OpenAiCompatibleImageDescriber("lmstudio:describe", "LM Studio", new OpenAiCompatibleEndpoint(new Uri("http://127.0.0.1:1234/v1"), "model"));

        Assert.Equal(DescriberPromptTemplates.Latest.Version, describer.PromptVersion);
        Assert.Equal("describe-1", describer.GetPromptVersion("describe-1"));
        Assert.Equal(DescriberPromptTemplates.Latest.Version, describer.GetPromptVersion("describe-unknown"));
    }
}
