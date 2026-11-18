using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.Prompts;
using MediaFileClassifier.Infrastructure.AI.StructuredOutput;
using Microsoft.Extensions.AI;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Describes images with a vision-language model on an OpenAI-compatible endpoint, such as Gemma in LM Studio. The
/// answer follows <see cref="ImageDescriptionContract"/>: the schema is sent as the response format, the answer is
/// validated and repaired, an unusable answer is retried once with the reason, and if that fails too the call throws
/// <see cref="AiProviderException"/> with <see cref="AiFailureKind.InvalidOutput"/> so the pipeline records a failed
/// run (E01-S05-T05).
/// </summary>
public sealed class OpenAiCompatibleImageDescriber : OpenAiCompatibleProvider, IMultiImageDescriber
{
    /// <summary>
    /// The most pictures a request carries by default (E06-S04-T07). Vision-language models served by LM Studio and
    /// Ollama take several images per message.
    /// </summary>
    public const int DefaultMaxImages = 6;

    // D350: Qwen recommends 1.5 for its vision-language instruct models; it stops the near-duplicate tags. Not used for OCR (D345).
    internal const float DescriberPresencePenalty = 1.5f;

    private const string SchemaDescription = "Description of an image.";

    // The schema as written, with its maxItems and maxLength. Microsoft.Extensions.AI rewrites a ChatResponseFormat
    // schema for OpenAI's strict mode, which moves those keywords into the descriptions; a server that builds a grammar
    // from the schema (llama.cpp, LM Studio) then lets a small model repeat one tag until it runs out of tokens. Handing
    // the OpenAI client its own response format keeps them. One per schema version the templates ask for.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, OpenAI.Chat.ChatResponseFormat> SchemaResponseFormats = new();

    private readonly bool useSchemaResponseFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleImageDescriber"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="displayName">Name shown to the user.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="httpClient">HTTP client, or <c>null</c> for the SDK's own.</param>
    /// <param name="useSchemaResponseFormat">
    /// Whether to send the schema as <c>response_format</c>. Turn it off for servers that reject it; the schema is
    /// then only in the prompt, and validation and repair still apply.
    /// </param>
    /// <param name="maxImages">The most pictures one request may carry, at least 2; see <see cref="DefaultMaxImages"/>.</param>
    public OpenAiCompatibleImageDescriber(
        string id,
        string displayName,
        OpenAiCompatibleEndpoint endpoint,
        HttpClient? httpClient = null,
        bool useSchemaResponseFormat = true,
        int maxImages = DefaultMaxImages)
        : base(id, displayName, endpoint, httpClient)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxImages, 2);
        this.useSchemaResponseFormat = useSchemaResponseFormat;
        MaxImages = maxImages;
    }

    /// <inheritdoc />
    public string PromptVersion => DescriberPromptTemplates.Latest.Version;

    /// <inheritdoc />
    public string GetPromptVersion(string? requested) => DescriberPromptTemplates.Resolve(requested).Version;

    /// <inheritdoc />
    /// <remarks>The structured output contract requires every field (E01-S05-T05).</remarks>
    public DescriberFields SupportedFields => DescriberFields.All;

    /// <inheritdoc />
    public int MaxImages { get; }

    /// <inheritdoc />
    public Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        return DescribeCoreAsync([image], options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AiOutput<ImageDescription>> DescribeFramesAsync(IReadOnlyList<AiImage> frames, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(options);
        if (frames.Count == 0 || frames.Count > MaxImages)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), frames.Count, $"A request carries 1 to {MaxImages} pictures.");
        }

        return DescribeCoreAsync(frames, options, cancellationToken);
    }

    private async Task<AiOutput<ImageDescription>> DescribeCoreAsync(IReadOnlyList<AiImage> images, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        // The template introduces several frames as one clip (options.FrameCount).
        var template = DescriberPromptTemplates.Resolve(options.PromptVersion);
        var responseFormat = SchemaResponseFormats.GetOrAdd(template.Schema, schema => OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
            ImageDescriptionContract.SchemaName,
            BinaryData.FromString(ImageDescriptionContract.GetSchema(schema).GetRawText()),
            SchemaDescription));
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, template.System),
            new(ChatRole.User, [new TextContent(template.BuildRequest(options)), .. images.Select(ToDataContent)]),
        };
        var chatOptions = new ChatOptions
        {
            Temperature = 0.2f,
            PresencePenalty = DescriberPresencePenalty,
            MaxOutputTokens = ImageDescriptionContract.MaxOutputTokens,
            ResponseFormat = useSchemaResponseFormat ? null : ChatResponseFormat.Json,
            RawRepresentationFactory = useSchemaResponseFormat ? _ => new OpenAI.Chat.ChatCompletionOptions { ResponseFormat = responseFormat } : null,
        };

        var (response, duration) = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
        var result = ImageDescriptionContract.Parse(response.Text, template.Schema);
        if (!result.IsSuccess)
        {
            // One retry, telling the model what was wrong.
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"That answer can't be used: {result.Error} Answer again with only the JSON object, with every field filled in."));
            var (retry, retryDuration) = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
            duration += retryDuration;
            result = ImageDescriptionContract.Parse(retry.Text, template.Schema);
            if (!result.IsSuccess)
            {
                throw new AiProviderException(
                    AiFailureKind.InvalidOutput,
                    Id,
                    $"{Model.ModelName} didn't return a usable description: {result.Error}",
                    retry.Text);
            }
        }

        return new AiOutput<ImageDescription>(result.Value!, Id, Model, template.Version, duration);
    }
}
