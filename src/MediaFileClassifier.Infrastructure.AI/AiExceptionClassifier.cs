using System.ClientModel;
using System.Text.Json;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.AI;

/// <summary>
/// Classifies AI provider failures by <see cref="AiProviderException.Kind"/> (E05-S04-T01, MFC-82 contract). A provider
/// that's unreachable, times out or answers with server errors is a provider failure for the circuit breaker; a
/// provider rejecting one input is an item failure. A missing model waits (D037).
/// </summary>
public sealed class AiExceptionClassifier : IExceptionClassifier
{
    /// <inheritdoc />
    public ClassifiedError? TryClassify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            AiProviderException provider => Classify(provider),

            // A model that fails on one input gives the same error next time.
            OnnxRuntimeException => new ClassifiedError(ErrorCategory.Permanent, exception.Message, exception),
            _ => null,
        };
    }

    private static ClassifiedError Classify(AiProviderException exception)
    {
        var (category, providerFailure) = exception.Kind switch
        {
            AiFailureKind.Unreachable or AiFailureKind.Timeout => (ErrorCategory.Transient, true),
            AiFailureKind.ProviderError => ClassifyProviderError(exception.InnerException),
            AiFailureKind.RemoteHostNotConfirmed => (ErrorCategory.Configuration, true),
            AiFailureKind.ModelMissing => (ErrorCategory.Configuration, false),
            _ => (ErrorCategory.Permanent, false),
        };

        return new ClassifiedError(category, exception.Message, exception)
        {
            ProviderId = exception.ProviderId,
            IsProviderFailure = providerFailure && exception.ProviderId is not null,
            IsModelMissing = exception.Kind == AiFailureKind.ModelMissing,
        };
    }

    private static (ErrorCategory Category, bool ProviderFailure) ClassifyProviderError(Exception? inner) => inner switch
    {
        // HTTP 4xx: the server is up and rejects this request, e.g. an image the model can't take.
        ClientResultException { Status: >= 400 and < 500 and not (408 or 429) } => (ErrorCategory.Permanent, false),
        HttpRequestException { StatusCode: { } status } when (int)status is >= 400 and < 500 and not (408 or 429) => (ErrorCategory.Permanent, false),

        // A local model failing on one input.
        OnnxRuntimeException => (ErrorCategory.Permanent, false),

        // Rate limits, server errors and replies that aren't OpenAI-compatible.
        ClientResultException or HttpRequestException or JsonException => (ErrorCategory.Transient, true),
        _ => (ErrorCategory.Transient, true),
    };
}
