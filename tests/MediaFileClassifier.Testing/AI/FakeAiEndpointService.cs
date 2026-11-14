using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// An <see cref="IAiEndpointService"/> that keeps endpoints in memory and answers connection tests from settable
/// results, so tests of the endpoint settings need no server. It records the tests it was asked for.
/// </summary>
public sealed class FakeAiEndpointService : IAiEndpointService
{
    private readonly List<AiEndpointSettings> endpoints = [];
    private readonly Dictionary<string, string> keys = [];
    private int nextId = 1;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<AiEndpointSettings> Endpoints => endpoints;

    /// <summary>
    /// Gets or sets the answer to a test of the LM Studio address.
    /// </summary>
    public EndpointTestResult LmStudio { get; set; } = new(EndpointTestStatus.Unreachable, [], "LM Studio isn't answering.");

    /// <summary>
    /// Gets or sets the answer to a test of any other address.
    /// </summary>
    public EndpointTestResult Result { get; set; } = new(EndpointTestStatus.Connected, ["model-a", "model-b"]);

    /// <summary>
    /// Gets the tests asked for: address, key and whether the remote host was confirmed.
    /// </summary>
    public List<(string BaseUrl, string? ApiKey, bool RemoteHostConfirmed)> Tests { get; } = [];

    /// <summary>
    /// Gets the API keys the last saves passed, by endpoint ID.
    /// </summary>
    public IReadOnlyDictionary<string, string> Keys => keys;

    /// <inheritdoc />
    public AiEndpointSettings Save(AiEndpointSettings endpoint, string? apiKey)
    {
        var stored = string.IsNullOrEmpty(endpoint.Id) ? endpoint with { Id = $"e{nextId++}" } : endpoint;
        var index = endpoints.FindIndex(existing => existing.Id == stored.Id);
        if (index < 0)
        {
            endpoints.Add(stored);
        }
        else
        {
            endpoints[index] = stored;
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            keys[stored.Id] = apiKey;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return stored;
    }

    /// <inheritdoc />
    public bool Remove(string id)
    {
        var removed = endpoints.RemoveAll(endpoint => endpoint.Id == id) > 0;
        keys.Remove(id);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <inheritdoc />
    public Task<EndpointTestResult> TestAsync(string baseUrl, string? apiKey, bool remoteHostConfirmed, CancellationToken cancellationToken, string? endpointId = null)
    {
        Tests.Add((baseUrl, apiKey ?? (endpointId is not null ? keys.GetValueOrDefault(endpointId) : null), remoteHostConfirmed));
        return Task.FromResult(baseUrl == IAiEndpointService.LmStudioBaseUrl ? LmStudio : Result);
    }

    /// <inheritdoc />
    public Task<EndpointTestResult> DetectLmStudioAsync(CancellationToken cancellationToken) =>
        TestAsync(IAiEndpointService.LmStudioBaseUrl, null, false, cancellationToken);

    /// <inheritdoc />
    public string? GetRemoteHostWarning(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) && url.Host is not ("localhost" or "127.0.0.1" or "[::1]")
            ? $"Your images will be sent to {url.Host} for analysis. Only continue if you trust this service."
            : null;

    /// <inheritdoc />
    public bool HasApiKey(string id) => keys.ContainsKey(id);
}
