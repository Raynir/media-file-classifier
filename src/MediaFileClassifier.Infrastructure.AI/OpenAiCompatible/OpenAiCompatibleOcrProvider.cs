using MediaFileClassifier.Infrastructure.Abstractions.AI;
using Microsoft.Extensions.AI;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Reads text in an image with a vision-language model on an OpenAI-compatible endpoint (D006). It's also the
/// minimal example of an <see cref="OpenAiCompatibleProvider"/>.
/// </summary>
/// <param name="id">Provider instance ID.</param>
/// <param name="displayName">Name shown to the user.</param>
/// <param name="endpoint">The endpoint.</param>
/// <param name="httpClient">HTTP client, or <c>null</c> for the SDK's own.</param>
public sealed class OpenAiCompatibleOcrProvider(string id, string displayName, OpenAiCompatibleEndpoint endpoint, HttpClient? httpClient = null)
    : OpenAiCompatibleProvider(id, displayName, endpoint, httpClient), IOcrProvider
{
    /// <summary>
    /// What the model answers when the image has no text.
    /// </summary>
    internal const string NoTextMarker = "<no text>";

    private const string PromptVersion = "ocr-1";

    private const string Prompt =
        "Transcribe all text visible in this image exactly as written, keeping line breaks. "
        + "Answer with the text only, without comments or formatting. If there is no text, answer " + NoTextMarker + ".";

    /// <inheritdoc />
    public async Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        ChatMessage[] messages = [new(ChatRole.User, [new TextContent(Prompt), new DataContent(image.Data, image.MediaType)])];
        var (response, duration) = await GetResponseAsync(messages, new ChatOptions { Temperature = 0 }, cancellationToken)
            .ConfigureAwait(false);

        var text = response.Text.Trim();
        if (text.Equals(NoTextMarker, StringComparison.OrdinalIgnoreCase))
        {
            text = string.Empty;
        }

        return new AiOutput<OcrResult>(new OcrResult(text), Id, Model, PromptVersion, duration);
    }
}
