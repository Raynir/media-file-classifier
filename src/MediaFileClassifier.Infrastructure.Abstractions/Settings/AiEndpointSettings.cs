namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// An OpenAI-compatible AI endpoint the user configured (E17-S02-T02), as stored in <see cref="AppSettings.AiEndpoints"/>.
/// The API key is not part of it: it is held in memory only until secure storage arrives (E06-S11-T04).
/// </summary>
/// <param name="Id">Stable ID, used in the provider IDs the analysis profile refers to.</param>
/// <param name="Name">Name shown to the user, e.g. "LM Studio".</param>
/// <param name="BaseUrl">Base URL including <c>/v1</c>, e.g. <c>http://127.0.0.1:1234/v1</c>.</param>
/// <param name="Model">Model name as the server knows it, or empty when none is chosen yet (the endpoint then offers no providers).</param>
/// <param name="RemoteHostConfirmed">Whether the user confirmed that images may be sent to a host that isn't this computer.</param>
public sealed record AiEndpointSettings(string Id, string Name, string BaseUrl, string Model, bool RemoteHostConfirmed = false);
