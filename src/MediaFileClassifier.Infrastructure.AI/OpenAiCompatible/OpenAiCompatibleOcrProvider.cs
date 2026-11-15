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

    /// <summary>
    /// The most tokens a transcription may take, so a model that repeats a line stops instead of filling its context.
    /// A dense page of text is about 1,500 tokens. An answer cut off here is cleaned by <see cref="OcrLoopTrimmer"/>.
    /// </summary>
    internal const int MaxOutputTokens = 2048;

    private const string PromptVersion = "ocr-1";

    private const string Prompt =
        "Transcribe all text visible in this image exactly as written, keeping line breaks. "
        + "Answer with the text only, without comments or formatting. If there is no text, answer " + NoTextMarker + ".";

    /// <inheritdoc />
    public async Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        ChatMessage[] messages = [new(ChatRole.User, [new TextContent(Prompt), ToDataContent(image)])];
        var (response, duration) = await GetResponseAsync(messages, new ChatOptions { Temperature = 0, MaxOutputTokens = MaxOutputTokens }, cancellationToken)
            .ConfigureAwait(false);

        var text = response.Text.Trim();
        if (response.FinishReason == ChatFinishReason.Length)
        {
            text = OcrLoopTrimmer.TrimCutOff(text);
        }

        if (OcrNoTextAnswer.Matches(text))
        {
            text = string.Empty;
        }

        return new AiOutput<OcrResult>(new OcrResult(text), Id, Model, PromptVersion, duration);
    }
}
