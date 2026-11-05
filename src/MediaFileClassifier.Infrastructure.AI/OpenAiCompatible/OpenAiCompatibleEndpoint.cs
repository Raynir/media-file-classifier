using System.Net;

namespace MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;

/// <summary>
/// Settings of an OpenAI-compatible endpoint, such as LM Studio (<c>http://127.0.0.1:1234/v1</c>), Ollama or a cloud
/// API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Privacy (MVP rule).</b> An endpoint whose host isn't this machine (<see cref="IsLocal"/>) receives the user's
/// images. The UI shows <see cref="GetRemoteHostWarning"/> and asks for explicit confirmation, and providers refuse to
/// send anything until <see cref="RemoteHostConfirmed"/> is set.
/// </para>
/// <para>
/// <b>The API key is held in memory only.</b> It is never written to settings or logs, and <see cref="ToString"/>
/// leaves it out, until secure storage arrives (E06-S11-T04).
/// </para>
/// </remarks>
public sealed class OpenAiCompatibleEndpoint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAiCompatibleEndpoint"/> class.
    /// </summary>
    /// <param name="baseUrl">Base URL of the API, including <c>/v1</c>, e.g. <c>http://127.0.0.1:1234/v1</c>.</param>
    /// <param name="model">Model name as the endpoint knows it, e.g. <c>google/gemma-4-e2b</c>.</param>
    public OpenAiCompatibleEndpoint(Uri baseUrl, string model)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The endpoint needs an absolute http or https URL.", nameof(baseUrl));
        }

        BaseUrl = baseUrl;
        Model = model;
    }

    public Uri BaseUrl { get; }

    public string Model { get; }

    /// <summary>
    /// Gets the API key, if the endpoint needs one. Held in memory only.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets how long one request may take. Local vision models can be slow, so the default is generous.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets how many times a request that failed with a transient error (connection lost, 429, 5xx) is retried.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Gets a value indicating whether the user confirmed sending images to a host that isn't local.
    /// </summary>
    public bool RemoteHostConfirmed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host is this machine: <c>localhost</c>, <c>127.0.0.1</c> or <c>::1</c>.
    /// </summary>
    public bool IsLocal => IsLocalHost(BaseUrl);

    /// <summary>
    /// Gets a value indicating whether providers may send images to the endpoint.
    /// </summary>
    public bool IsAllowed => IsLocal || RemoteHostConfirmed;

    /// <summary>
    /// Checks whether a URL points at this machine.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns><c>true</c> for <c>localhost</c> and the loopback addresses.</returns>
    public static bool IsLocalHost(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var host = url.IdnHost;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }

    /// <summary>
    /// Gets the warning the UI shows before the user confirms a host that isn't local.
    /// </summary>
    /// <returns>The warning, or <c>null</c> for a local endpoint.</returns>
    public string? GetRemoteHostWarning() =>
        IsLocal ? null : $"Your images will be sent to {BaseUrl.Host} for analysis. Only continue if you trust this service.";

    /// <inheritdoc />
    public override string ToString() => $"{BaseUrl} ({Model})";
}
