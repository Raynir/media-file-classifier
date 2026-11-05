namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// What the registry knows about one provider: its capabilities and its health when last checked.
/// </summary>
/// <param name="ProviderId">Provider instance ID.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="Kind">How the provider runs its model.</param>
/// <param name="Model">The model.</param>
/// <param name="Capabilities">Capabilities the provider implements.</param>
/// <param name="Health">Health when checked.</param>
/// <param name="CheckedAt">When the health was checked.</param>
public sealed record ProviderStatus(
    string ProviderId,
    string DisplayName,
    AiProviderKind Kind,
    ModelIdentity Model,
    IReadOnlyList<AiCapability> Capabilities,
    ProviderHealth Health,
    DateTimeOffset CheckedAt)
{
    /// <summary>
    /// Gets a value indicating whether the provider can be used now.
    /// </summary>
    public bool IsAvailable => Health.IsHealthy;
}
