namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Health of one provider.
/// </summary>
/// <param name="Status">The status.</param>
/// <param name="Message">What's wrong and what to do about it, for the user. <c>null</c> when healthy.</param>
/// <param name="Detail">Technical detail for the log, such as an exception message.</param>
public sealed record ProviderHealth(ProviderHealthStatus Status, string? Message = null, string? Detail = null)
{
    /// <summary>
    /// Gets the result for a provider that's ready to use.
    /// </summary>
    public static ProviderHealth Healthy { get; } = new(ProviderHealthStatus.Healthy);

    /// <summary>
    /// Gets a value indicating whether the provider is ready to use.
    /// </summary>
    public bool IsHealthy => Status == ProviderHealthStatus.Healthy;

    /// <summary>
    /// Gets a value indicating whether jobs that need this provider should wait rather than fail (D037).
    /// </summary>
    public bool IsWaitingForModel => Status == ProviderHealthStatus.ModelMissing;
}
