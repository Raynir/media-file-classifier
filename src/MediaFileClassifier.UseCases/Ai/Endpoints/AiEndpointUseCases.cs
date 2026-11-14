using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Ai.Endpoints;

/// <summary>
/// Lists the configured OpenAI-compatible endpoints (E17-S02-T02).
/// </summary>
public sealed record ListAiEndpointsQuery : IRequest<IReadOnlyList<AiEndpointSettings>>;

/// <summary>
/// Handles <see cref="ListAiEndpointsQuery"/>.
/// </summary>
/// <param name="endpoints">The endpoint service.</param>
public sealed class ListAiEndpointsQueryHandler(IAiEndpointService endpoints)
    : IRequestHandler<ListAiEndpointsQuery, IReadOnlyList<AiEndpointSettings>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<AiEndpointSettings>> Handle(ListAiEndpointsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(endpoints.Endpoints);
}

/// <summary>
/// Adds or changes an endpoint. The API key stays in memory.
/// </summary>
/// <param name="Endpoint">The endpoint; an empty ID adds a new one.</param>
/// <param name="ApiKey">The key; <c>null</c> keeps the one held, empty clears it.</param>
public sealed record SaveAiEndpointCommand(AiEndpointSettings Endpoint, string? ApiKey) : IRequest<AiEndpointSettings>;

/// <summary>
/// Handles <see cref="SaveAiEndpointCommand"/>.
/// </summary>
/// <param name="endpoints">The endpoint service.</param>
public sealed class SaveAiEndpointCommandHandler(IAiEndpointService endpoints)
    : IRequestHandler<SaveAiEndpointCommand, AiEndpointSettings>
{
    /// <inheritdoc />
    public Task<AiEndpointSettings> Handle(SaveAiEndpointCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(endpoints.Save(request.Endpoint, request.ApiKey));
    }
}

/// <summary>
/// Removes an endpoint and its providers.
/// </summary>
/// <param name="Id">The endpoint ID.</param>
public sealed record RemoveAiEndpointCommand(string Id) : IRequest<bool>;

/// <summary>
/// Handles <see cref="RemoveAiEndpointCommand"/>.
/// </summary>
/// <param name="endpoints">The endpoint service.</param>
public sealed class RemoveAiEndpointCommandHandler(IAiEndpointService endpoints)
    : IRequestHandler<RemoveAiEndpointCommand, bool>
{
    /// <inheritdoc />
    public Task<bool> Handle(RemoveAiEndpointCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(endpoints.Remove(request.Id));
    }
}

/// <summary>
/// Tests a connection by listing the server's models.
/// </summary>
/// <param name="BaseUrl">The base URL as typed.</param>
/// <param name="ApiKey">The API key, or <c>null</c>.</param>
/// <param name="RemoteHostConfirmed">Whether the user confirmed a host that isn't this computer.</param>
public sealed record TestAiEndpointQuery(string BaseUrl, string? ApiKey, bool RemoteHostConfirmed) : IRequest<EndpointTestResult>;

/// <summary>
/// Handles <see cref="TestAiEndpointQuery"/>.
/// </summary>
/// <param name="endpoints">The endpoint service.</param>
public sealed class TestAiEndpointQueryHandler(IAiEndpointService endpoints)
    : IRequestHandler<TestAiEndpointQuery, EndpointTestResult>
{
    /// <inheritdoc />
    public Task<EndpointTestResult> Handle(TestAiEndpointQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return endpoints.TestAsync(request.BaseUrl, request.ApiKey, request.RemoteHostConfirmed, cancellationToken);
    }
}

/// <summary>
/// Looks for LM Studio's local server and lists its models.
/// </summary>
public sealed record DetectLmStudioQuery : IRequest<EndpointTestResult>;

/// <summary>
/// Handles <see cref="DetectLmStudioQuery"/>.
/// </summary>
/// <param name="endpoints">The endpoint service.</param>
public sealed class DetectLmStudioQueryHandler(IAiEndpointService endpoints)
    : IRequestHandler<DetectLmStudioQuery, EndpointTestResult>
{
    /// <inheritdoc />
    public Task<EndpointTestResult> Handle(DetectLmStudioQuery request, CancellationToken cancellationToken) =>
        endpoints.DetectLmStudioAsync(cancellationToken);
}
