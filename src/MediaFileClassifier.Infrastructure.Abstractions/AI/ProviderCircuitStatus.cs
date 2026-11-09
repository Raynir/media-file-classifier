namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// An AI provider whose circuit is open (E05-S04-T03): work that needs it waits until it's available again.
/// </summary>
/// <param name="ProviderId">Provider instance ID.</param>
/// <param name="DisplayName">Name shown to the user, e.g. "LM Studio".</param>
/// <param name="Message">What's wrong, for the user, e.g. "http://localhost:1234/v1 isn't answering.".</param>
/// <param name="OpenedAt">When the circuit opened.</param>
/// <param name="IsConfiguration">Whether the provider needs the user to change a setting, rather than to come back.</param>
public sealed record ProviderCircuitStatus(
    string ProviderId,
    string DisplayName,
    string Message,
    DateTimeOffset OpenedAt,
    bool IsConfiguration);
