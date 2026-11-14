using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// The outcome of a connection test.
/// </summary>
public enum EndpointTestStatus
{
    /// <summary>The server answered and listed its models.</summary>
    Connected,

    /// <summary>Nothing answered at the URL.</summary>
    Unreachable,

    /// <summary>Something answered, but not like an OpenAI-compatible server, or it rejected the request.</summary>
    Failed,

    /// <summary>The host isn't this computer and the user hasn't confirmed it, so nothing was sent.</summary>
    RemoteNotConfirmed,

    /// <summary>The URL isn't a valid http or https URL.</summary>
    InvalidUrl,
}

/// <summary>
/// The result of testing an endpoint.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Models">The models the server lists, sorted; empty unless connected.</param>
/// <param name="Message">What's wrong, for the user; <c>null</c> when connected.</param>
public sealed record EndpointTestResult(EndpointTestStatus Status, IReadOnlyList<string> Models, string? Message = null)
{
    /// <summary>
    /// Gets a value indicating whether the server answered.
    /// </summary>
    public bool IsConnected => Status == EndpointTestStatus.Connected;
}

/// <summary>
/// Manages the user's OpenAI-compatible endpoints (E17-S02-T02): stores them in the app settings, turns each into
/// providers in <see cref="IAiProviderRegistry"/>, and tests connections. API keys are held in memory only.
/// </summary>
public interface IAiEndpointService
{
    /// <summary>
    /// The base URL of LM Studio's local server.
    /// </summary>
    public const string LmStudioBaseUrl = "http://127.0.0.1:1234/v1";

    /// <summary>
    /// Raised after the configured endpoints changed.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets the configured endpoints, in the order they were added.
    /// </summary>
    IReadOnlyList<AiEndpointSettings> Endpoints { get; }

    /// <summary>
    /// Adds an endpoint or replaces the one with the same ID, and registers its providers when it has a model.
    /// </summary>
    /// <param name="endpoint">The endpoint; an empty ID gets a new one.</param>
    /// <param name="apiKey">The API key, kept in memory only; <c>null</c> keeps the one held, empty clears it.</param>
    /// <returns>The stored endpoint.</returns>
    /// <exception cref="ArgumentException">The name is empty or the URL isn't a valid http or https URL.</exception>
    AiEndpointSettings Save(AiEndpointSettings endpoint, string? apiKey);

    /// <summary>
    /// Removes an endpoint and its providers.
    /// </summary>
    /// <param name="id">The endpoint ID.</param>
    /// <returns><c>true</c> if it existed.</returns>
    bool Remove(string id);

    /// <summary>
    /// Tests a connection by listing the server's models. Nothing is sent to a remote host that isn't confirmed.
    /// </summary>
    /// <param name="baseUrl">Base URL as typed; <c>/v1</c> is added when the URL has no path.</param>
    /// <param name="apiKey">The API key, or <c>null</c>.</param>
    /// <param name="remoteHostConfirmed">Whether the user confirmed a host that isn't this computer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="endpointId">A saved endpoint whose held key is used when <paramref name="apiKey"/> is empty.</param>
    /// <returns>The result. Never throws for a server that's down.</returns>
    Task<EndpointTestResult> TestAsync(string baseUrl, string? apiKey, bool remoteHostConfirmed, CancellationToken cancellationToken, string? endpointId = null);

    /// <summary>
    /// Looks for LM Studio's local server at <see cref="LmStudioBaseUrl"/> and lists its models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the test.</returns>
    Task<EndpointTestResult> DetectLmStudioAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the warning to show before the user confirms a host that isn't this computer.
    /// </summary>
    /// <param name="baseUrl">Base URL as typed.</param>
    /// <returns>The warning, or <c>null</c> for a local or invalid URL.</returns>
    string? GetRemoteHostWarning(string baseUrl);

    /// <summary>
    /// Gets a value indicating whether a key is held in memory for an endpoint.
    /// </summary>
    /// <param name="id">The endpoint ID.</param>
    /// <returns><c>true</c> if there is one.</returns>
    bool HasApiKey(string id);
}
