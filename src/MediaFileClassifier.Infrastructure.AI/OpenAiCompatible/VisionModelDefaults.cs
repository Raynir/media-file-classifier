namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Built-in defaults for vision-language models on an OpenAI-compatible server (E06-S04-T08). The app doesn't download
/// these models (the server does), so they aren't in the model catalogue; an endpoint's own
/// <see cref="Abstractions.Settings.AiEndpointSettings.MaxImages"/> overrides the default.
/// </summary>
/// <remarks>
/// D367: <c>qwen/qwen3-vl-4b</c> is the default describer model, <c>google/gemma-4-e2b</c> the one for GPUs with less
/// memory. Both families take several pictures in one message. Any other model gets one picture per request, so a model
/// that rejects several never fails a clip; its clips are described frame by frame and merged.
/// </remarks>
public static class VisionModelDefaults
{
    /// <summary>
    /// The default describer model in LM Studio (D367).
    /// </summary>
    public const string DefaultModel = "qwen/qwen3-vl-4b";

    /// <summary>
    /// The describer model for GPUs with less memory (D367).
    /// </summary>
    public const string LowMemoryModel = "google/gemma-4-e2b";

    // Model families known to take several pictures per message, matched at the start of the name without the publisher.
    private static readonly string[] MultiImageFamilies = ["qwen3-vl", "gemma-4"];

    /// <summary>
    /// Gets how many pictures one describer request carries for a model when its endpoint sets none.
    /// </summary>
    /// <param name="model">Model name as the server knows it, e.g. <c>qwen/qwen3-vl-4b</c>.</param>
    /// <returns><see cref="OpenAiCompatibleMultiImageDescriber.DefaultMaxImages"/> for a known multi-image family, else 1.</returns>
    public static int GetMaxImages(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return 1;
        }

        var name = model.Trim();
        var slash = name.LastIndexOf('/');
        name = slash >= 0 ? name[(slash + 1)..] : name;
        return MultiImageFamilies.Any(family => name.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            ? OpenAiCompatibleMultiImageDescriber.DefaultMaxImages
            : 1;
    }

    /// <summary>
    /// Gets how many pictures one describer request carries for an endpoint: its own setting when it has a usable one,
    /// else the model's default.
    /// </summary>
    /// <param name="model">Model name as the server knows it.</param>
    /// <param name="configured">The endpoint's setting, or <c>null</c>.</param>
    /// <returns>At least 1.</returns>
    public static int Resolve(string? model, int? configured) => configured is >= 1 ? configured.Value : GetMaxImages(model);
}
