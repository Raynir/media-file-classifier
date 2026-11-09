using System.Globalization;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
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
public sealed class OpenAiCompatibleImageDescriber : OpenAiCompatibleProvider, IImageDescriber
{
    private const string SystemPrompt =
        "You describe photos and video frames for a personal media library, so they can be found by searching. "
        + "Describe only what is visible. Answer with a single JSON object that matches the schema, and nothing else.";

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        ImageDescriptionContract.Schema,
        ImageDescriptionContract.SchemaName,
        "Description of an image.");

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
    public OpenAiCompatibleImageDescriber(
        string id,
        string displayName,
        OpenAiCompatibleEndpoint endpoint,
        HttpClient? httpClient = null,
        bool useSchemaResponseFormat = true)
        : base(id, displayName, endpoint, httpClient)
    {
        this.useSchemaResponseFormat = useSchemaResponseFormat;
    }

    /// <inheritdoc />
    public string PromptVersion => ImageDescriptionContract.Version;

    /// <inheritdoc />
    public async Task<AiOutput<ImageDescription>> DescribeAsync(AiImage image, ImageDescriptionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, [new TextContent(BuildRequest(options)), ToDataContent(image)]),
        };
        var chatOptions = new ChatOptions
        {
            Temperature = 0.2f,
            ResponseFormat = useSchemaResponseFormat ? ResponseFormat : ChatResponseFormat.Json,
        };

        var (response, duration) = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
        var result = ImageDescriptionContract.Parse(response.Text);
        if (!result.IsSuccess)
        {
            // One retry, telling the model what was wrong.
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"That answer can't be used: {result.Error} Answer again with only the JSON object, with every field filled in."));
            var (retry, retryDuration) = await GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
            duration += retryDuration;
            result = ImageDescriptionContract.Parse(retry.Text);
            if (!result.IsSuccess)
            {
                throw new AiProviderException(
                    AiFailureKind.InvalidOutput,
                    Id,
                    $"{Model.ModelName} didn't return a usable description: {result.Error}",
                    retry.Text);
            }
        }

        return new AiOutput<ImageDescription>(result.Value!, Id, Model, PromptVersion, duration);
    }

    private static string BuildRequest(ImageDescriptionOptions options)
    {
        var language = GetLanguageName(options.Language);
        var request = $"Describe this image. Write all text in {language}. "
            + "Fields: title, description, summary, tags, objects, scenes, actions, emotions. "
            + $"Use JSON lists of short phrases for the last five, at most {ImageDescriptionContract.MaxListItems} each.";
        return options.Context is null ? request : $"{request} Context: {options.Context}";
    }

    private static string GetLanguageName(string? code)
    {
        if (code is null)
        {
            return "English";
        }

        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
