using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Base class for providers that call an OpenAI-compatible endpoint (D005). It talks to the endpoint through
/// Microsoft.Extensions.AI's <see cref="IChatClient"/>, applies the timeout and retry settings, enforces the privacy
/// rule for hosts that aren't local, and turns transport errors into <see cref="AiProviderException"/>.
/// </summary>
/// <remarks>
/// <see cref="CheckHealthAsync"/> asks the endpoint for its models (<c>GET /models</c>): it's unreachable if that
/// fails, and also if the configured model isn't listed.
/// </remarks>
public abstract class OpenAiCompatibleProvider : IAiProvider, IDisposable
{
    /// <summary>
    /// The JPEG quality of raw pictures sent to the endpoint: high enough that small text stays readable for OCR.
    /// </summary>
    private const int JpegQuality = 90;

    private readonly OpenAIClient client;
    private readonly IChatClient chatClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleProvider"/> class.
    /// </summary>
    /// <param name="id">Provider instance ID.</param>
    /// <param name="displayName">Name shown to the user.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="httpClient">HTTP client to send requests with, or <c>null</c> for the SDK's own. The caller owns it.</param>
    protected OpenAiCompatibleProvider(string id, string displayName, OpenAiCompatibleEndpoint endpoint, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(endpoint);

        Id = id;
        DisplayName = displayName;
        Endpoint = endpoint;
        Model = new ModelIdentity(ProviderIds.OpenAiCompatible, endpoint.Model);

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint.BaseUrl,
            NetworkTimeout = endpoint.Timeout,
            RetryPolicy = new ClientRetryPolicy(endpoint.MaxRetries),
        };
        if (httpClient is not null)
        {
            options.Transport = new HttpClientPipelineTransport(httpClient);
        }

        // Local servers ignore the key, but the SDK requires one.
        client = new OpenAIClient(new ApiKeyCredential(string.IsNullOrWhiteSpace(endpoint.ApiKey) ? "not-needed" : endpoint.ApiKey), options);
        chatClient = client.GetChatClient(endpoint.Model).AsIChatClient();
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public AiProviderKind Kind => AiProviderKind.OpenAiCompatible;

    /// <inheritdoc />
    public ModelIdentity Model { get; }

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform => true;

    public OpenAiCompatibleEndpoint Endpoint { get; }

    /// <summary>
    /// Gets the chat client. Use <see cref="GetResponseAsync"/> to call it with error handling.
    /// </summary>
    protected IChatClient ChatClient => chatClient;

    /// <inheritdoc />
    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (!Endpoint.IsAllowed)
        {
            return new ProviderHealth(
                ProviderHealthStatus.RemoteHostNotConfirmed,
                $"Confirm that images may be sent to {Endpoint.BaseUrl.Host} before using it.");
        }

        IReadOnlyList<string> models;
        try
        {
            var result = await client.GetOpenAIModelClient().GetModelsAsync(cancellationToken).ConfigureAwait(false);
            models = result.Value.Select(model => model.Id).ToList();
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return new ProviderHealth(
                ProviderHealthStatus.Unreachable,
                $"{Endpoint.BaseUrl} isn't answering. Check that the server is running.",
                ex.Message);
        }

        return models.Contains(Endpoint.Model, StringComparer.OrdinalIgnoreCase)
            ? ProviderHealth.Healthy
            : new ProviderHealth(
                ProviderHealthStatus.Unreachable,
                $"{Endpoint.BaseUrl} doesn't serve the model {Endpoint.Model}. Load it in the server, or choose another model.",
                $"Served: {string.Join(", ", models)}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sends a chat request, after checking the privacy rule.
    /// </summary>
    /// <param name="messages">The messages.</param>
    /// <param name="options">Chat options, such as the response format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response and how long it took.</returns>
    /// <exception cref="AiProviderException">The host isn't allowed, or the request failed.</exception>
    protected async Task<(ChatResponse Response, TimeSpan Duration)> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (!Endpoint.IsAllowed)
        {
            throw new AiProviderException(
                AiFailureKind.RemoteHostNotConfirmed,
                Id,
                $"Images can't be sent to {Endpoint.BaseUrl.Host} until you confirm it.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            return (response, stopwatch.Elapsed);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw ToProviderException(ex);
        }
    }

    /// <summary>
    /// Turns an image into chat content. Raw RGBA pixels, as the analysis pipeline sends them (E06-S01), are encoded
    /// as JPEG first, since an endpoint only accepts encoded images.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The content.</returns>
    protected DataContent ToDataContent(AiImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!image.IsRawRgba)
        {
            return new DataContent(image.Data, image.MediaType);
        }

        if (image.PixelWidth is not { } width || image.PixelHeight is not { } height)
        {
            throw new AiProviderException(AiFailureKind.InvalidInput, Id, "Raw pixels need their width and height.");
        }

        // The pipeline's pictures are opaque (composited over white), so JPEG loses nothing but compression.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var source = SKImage.FromPixelCopy(info, image.Data.Span)
            ?? throw new AiProviderException(AiFailureKind.InvalidInput, Id, "The image couldn't be encoded.");
        using var data = source.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new AiProviderException(AiFailureKind.InvalidInput, Id, "The image couldn't be encoded.");
        return new DataContent(data.ToArray(), "image/jpeg");
    }

    /// <summary>
    /// Releases the chat client.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            chatClient.Dispose();
        }
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is ClientResultException or HttpRequestException or IOException or System.Text.Json.JsonException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private AiProviderException ToProviderException(Exception ex) => ex switch
    {
        OperationCanceledException => new AiProviderException(
            AiFailureKind.Timeout, Id, $"{Endpoint.BaseUrl} didn't answer within {Endpoint.Timeout.TotalSeconds:0} seconds.", innerException: ex),
        ClientResultException { Status: 408 or 504 } => new AiProviderException(
            AiFailureKind.Timeout, Id, $"{Endpoint.BaseUrl} timed out.", innerException: ex),
        ClientResultException { Status: > 0 } result => new AiProviderException(
            AiFailureKind.ProviderError, Id, $"{Endpoint.BaseUrl} returned an error ({result.Status}): {ex.Message}", innerException: ex),
        System.Text.Json.JsonException => new AiProviderException(
            AiFailureKind.ProviderError, Id, $"{Endpoint.BaseUrl} sent a reply that isn't OpenAI-compatible.", innerException: ex),
        _ => new AiProviderException(
            AiFailureKind.Unreachable, Id, $"{Endpoint.BaseUrl} isn't answering.", innerException: ex),
    };
}
