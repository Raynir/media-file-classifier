using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.AI.Describers;

/// <summary>
/// Names of the in-process describer (E06-S04-T01, MFC-105): Microsoft's Florence-2 (MIT), the onnx-community
/// export of the fine-tuned base and large models, fp32, run on the CPU. Its merged decoder writes wrong tokens on
/// DirectML, so no GPU execution provider is used.
/// </summary>
public static class Florence2Model
{
    /// <summary>
    /// The model version in the catalogue and the model store.
    /// </summary>
    public const string Version = "1.0";

    /// <summary>
    /// The version of the task prompts, decoding and field mapping, stored with each run.
    /// </summary>
    public const string PromptVersion = "florence-1";

    /// <summary>
    /// The role of the vision encoder file.
    /// </summary>
    public const string VisionRole = "vision";

    /// <summary>
    /// The role of the token embedding file.
    /// </summary>
    public const string EmbedRole = "embed";

    /// <summary>
    /// The role of the text encoder file.
    /// </summary>
    public const string EncoderRole = "encoder";

    /// <summary>
    /// The role of the merged decoder file (with and without the key-value cache).
    /// </summary>
    public const string DecoderRole = "decoder";

    /// <summary>
    /// The role of the tokenizer file (<c>tokenizer.json</c>).
    /// </summary>
    public const string TokenizerRole = "tokenizer";

    /// <summary>
    /// The fields Florence-2 writes: a detailed caption as the description, a short caption as the summary, and the
    /// detected objects' labels as objects and tags. No title, scenes, actions, emotions or Russian forms.
    /// </summary>
    public const DescriberFields SupportedFields = DescriberFields.Description | DescriberFields.Summary | DescriberFields.Tags | DescriberFields.Objects;

    /// <summary>
    /// Gets the base model, the default in-process describer (5.9 s per image on the CPU in MFC-105).
    /// </summary>
    public static Florence2Variant Base { get; } = new("florence2-base-ft", "onnx:florence2-base-ft", "Florence-2 base (in-process)");

    /// <summary>
    /// Gets the large model, chosen in the analysis profile (somewhat better, 10.8 s per image on the CPU).
    /// </summary>
    public static Florence2Variant Large { get; } = new("florence2-large-ft", "onnx:florence2-large-ft", "Florence-2 large (in-process)");
}

/// <summary>
/// One Florence-2 model size.
/// </summary>
/// <param name="ModelId">The model ID in the catalogue and the model store.</param>
/// <param name="ProviderId">The provider ID.</param>
/// <param name="DisplayName">The name shown to the user.</param>
public sealed record Florence2Variant(string ModelId, string ProviderId, string DisplayName);

/// <summary>
/// Settings of the in-process describer.
/// </summary>
public sealed record Florence2Options
{
    /// <summary>
    /// Gets the most tokens of the detailed caption (the description).
    /// </summary>
    public int DescriptionMaxTokens { get; init; } = 256;

    /// <summary>
    /// Gets the most tokens of the short caption (the summary).
    /// </summary>
    public int SummaryMaxTokens { get; init; } = 64;

    /// <summary>
    /// Gets the most tokens of the object detection answer, which spends four location tokens per object.
    /// </summary>
    public int ObjectsMaxTokens { get; init; } = 512;

    /// <summary>
    /// Gets the most tags taken from the detected objects.
    /// </summary>
    public int MaxTags { get; init; } = 15;
}
