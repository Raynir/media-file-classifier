using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Default <see cref="IAiEndpointService"/>. Each endpoint with a model becomes two providers, a describer and an OCR
/// provider (<c>openai-compatible:{id}:describer</c> and <c>:ocr</c>), so the analysis profile can pick them. The
/// endpoints live in <see cref="AppSettings.AiEndpoints"/>; API keys only in memory.
/// </summary>
/// <remarks>
/// Depends on <see cref="ISettingsService"/> and not on the settings store, so it adds no cycle through the library
/// session (docs/testing.md, the DI-cycle rule).
/// </remarks>
public sealed partial class AiEndpointService : IAiEndpointService, IDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private readonly ISettingsService settings;
    private readonly IAiProviderRegistry registry;
    private readonly ILogger logger;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Lock gate = new();
    private readonly ConcurrentDictionary<string, string> apiKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<OpenAiCompatibleProvider>> providersById = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AiEndpointService"/> class and registers the providers of the
    /// stored endpoints.
    /// </summary>
    /// <param name="settings">The app settings.</param>
    /// <param name="registry">The provider registry.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="http">HTTP client for connection tests, or <c>null</c> to create one. The caller owns it.</param>
    public AiEndpointService(
        ISettingsService settings,
        IAiProviderRegistry registry,
        ILogger<AiEndpointService>? logger = null,
        HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registry);

        this.settings = settings;
        this.registry = registry;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        this.http = http ?? new HttpClient { Timeout = TestTimeout };
        ownsHttp = http is null;
        foreach (var endpoint in Endpoints)
        {
            try
            {
                TryRegister(endpoint);
            }
            catch (ArgumentException ex)
            {
                // A hand-edited file with a repeated ID must not stop the app from starting.
                LogRegisterFailed(ex, endpoint.Id);
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<AiEndpointSettings> Endpoints => settings.Current.AiEndpoints ?? [];

    /// <inheritdoc />
    public AiEndpointSettings Save(AiEndpointSettings endpoint, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(endpoint.Name))
        {
            throw new ArgumentException("An endpoint needs a name.", nameof(endpoint));
        }

        var url = NormalizeBaseUrl(endpoint.BaseUrl) ?? throw new ArgumentException("The endpoint needs an http or https URL.", nameof(endpoint));
        var stored = new AiEndpointSettings(
            string.IsNullOrWhiteSpace(endpoint.Id) ? Guid.NewGuid().ToString("N") : endpoint.Id,
            endpoint.Name.Trim(),
            url.ToString(),
            endpoint.Model?.Trim() ?? string.Empty,
            RemoteHostConfirmed: !OpenAiCompatibleEndpoint.IsLocalHost(url) && endpoint.RemoteHostConfirmed,
            MaxImages: endpoint.MaxImages is >= 1 ? endpoint.MaxImages : null);

        lock (gate)
        {
            if (apiKey is not null)
            {
                if (apiKey.Length == 0)
                {
                    apiKeys.TryRemove(stored.Id, out _);
                }
                else
                {
                    apiKeys[stored.Id] = apiKey;
                }
            }

            settings.Update(current =>
            {
                var list = (current.AiEndpoints ?? []).ToList();
                var index = list.FindIndex(existing => existing.Id == stored.Id);
                if (index < 0)
                {
                    list.Add(stored);
                }
                else
                {
                    list[index] = stored;
                }

                return current with { AiEndpoints = list };
            });
            Unregister(stored.Id);
            TryRegister(stored);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return stored;
    }

    /// <inheritdoc />
    public bool Remove(string id)
    {
        bool existed;
        lock (gate)
        {
            existed = Endpoints.Any(endpoint => endpoint.Id == id);
            if (existed)
            {
                settings.Update(current => current with { AiEndpoints = (current.AiEndpoints ?? []).Where(endpoint => endpoint.Id != id).ToList() });
            }

            Unregister(id);
            apiKeys.TryRemove(id, out _);
        }

        if (existed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return existed;
    }

    /// <inheritdoc />
    public bool HasApiKey(string id) => apiKeys.ContainsKey(id);

    /// <inheritdoc />
    public string? GetRemoteHostWarning(string baseUrl) =>
        NormalizeBaseUrl(baseUrl) is { } url ? new OpenAiCompatibleEndpoint(url, "model").GetRemoteHostWarning() : null;

    /// <inheritdoc />
    public Task<EndpointTestResult> DetectLmStudioAsync(CancellationToken cancellationToken) =>
        TestAsync(IAiEndpointService.LmStudioBaseUrl, null, false, cancellationToken);

    /// <inheritdoc />
    public async Task<EndpointTestResult> TestAsync(string baseUrl, string? apiKey, bool remoteHostConfirmed, CancellationToken cancellationToken, string? endpointId = null)
    {
        var url = NormalizeBaseUrl(baseUrl);
        if (url is null)
        {
            return new EndpointTestResult(EndpointTestStatus.InvalidUrl, [], "Enter a full address, such as http://127.0.0.1:1234/v1.");
        }

        if (!OpenAiCompatibleEndpoint.IsLocalHost(url) && !remoteHostConfirmed)
        {
            return new EndpointTestResult(EndpointTestStatus.RemoteNotConfirmed, [], $"Confirm that images may be sent to {url.Host} before testing it.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url.ToString().TrimEnd('/') + "/models"));
        if (string.IsNullOrWhiteSpace(apiKey) && endpointId is not null)
        {
            apiKey = apiKeys.GetValueOrDefault(endpointId);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new EndpointTestResult(EndpointTestStatus.Failed, [], $"{url.Host} rejected the API key. Check the key and try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new EndpointTestResult(
                    EndpointTestStatus.Failed,
                    [],
                    $"{url.Host} answered with status {(int)response.StatusCode}. Check that the address ends with /v1.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new EndpointTestResult(EndpointTestStatus.Connected, ParseModels(json));
        }
        catch (JsonException)
        {
            return new EndpointTestResult(EndpointTestStatus.Failed, [], $"{url.Host} didn't answer like an OpenAI-compatible server. Check the address.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            LogTestFailed(ex, url);
            return new EndpointTestResult(EndpointTestStatus.Unreachable, [], $"{url} isn't answering. Check that the server is running.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var id in providersById.Keys.ToList())
            {
                Unregister(id);
            }
        }

        if (ownsHttp)
        {
            http.Dispose();
        }
    }

    /// <summary>
    /// Makes a typed URL a base URL: http or https, and <c>/v1</c> when the URL has no path.
    /// </summary>
    /// <param name="text">The URL as typed.</param>
    /// <returns>The URL, or <c>null</c> when it isn't a valid http or https URL.</returns>
    internal static Uri? NormalizeBaseUrl(string? text)
    {
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return url.AbsolutePath == "/" ? new Uri(url, "/v1") : url;
    }

    private static List<string> ParseModels(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("No model list.");
        }

        return data.EnumerateArray()
            .Select(model => model.ValueKind == JsonValueKind.Object && model.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void TryRegister(AiEndpointSettings stored)
    {
        if (string.IsNullOrWhiteSpace(stored.Model) || NormalizeBaseUrl(stored.BaseUrl) is not { } url)
        {
            return;
        }

        var endpoint = new OpenAiCompatibleEndpoint(url, stored.Model)
        {
            ApiKey = apiKeys.GetValueOrDefault(stored.Id),
            RemoteHostConfirmed = stored.RemoteHostConfirmed,
        };
        var name = $"{stored.Name} ({stored.Model})";
        var created = new List<OpenAiCompatibleProvider>
        {
            OpenAiCompatibleImageDescriber.Create(ProviderId(stored.Id, "describer"), name, endpoint, VisionModelDefaults.Resolve(stored.Model, stored.MaxImages)),
            new OpenAiCompatibleOcrProvider(ProviderId(stored.Id, "ocr"), name, endpoint),
        };
        providersById[stored.Id] = created;
        foreach (var provider in created)
        {
            registry.Register(provider);
        }
    }

    private void Unregister(string id)
    {
        if (!providersById.Remove(id, out var providers))
        {
            return;
        }

        foreach (var provider in providers)
        {
            registry.Unregister(provider.Id);
            provider.Dispose();
        }
    }

    private static string ProviderId(string endpointId, string capability) => $"{ProviderIds.OpenAiCompatible}:{endpointId}:{capability}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "The endpoint {EndpointId} couldn't be registered")]
    private partial void LogRegisterFailed(Exception exception, string endpointId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Testing the endpoint {Url} failed")]
    private partial void LogTestFailed(Exception exception, Uri url);
}
