using System.Collections.Concurrent;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.AI;

/// <summary>
/// Thread-safe <see cref="IAiProviderRegistry"/>. A health check never throws: a provider whose check fails is
/// reported as <see cref="ProviderHealthStatus.Unhealthy"/>.
/// </summary>
public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly Lock providersLock = new();
    private readonly ConcurrentDictionary<string, ProviderStatus> lastHealth = new(StringComparer.Ordinal);
    private IReadOnlyList<IAiProvider> providers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AiProviderRegistry"/> class.
    /// </summary>
    /// <param name="providers">Providers registered in dependency injection.</param>
    /// <param name="timeProvider">Clock for check times.</param>
    /// <param name="logger">Logger.</param>
    public AiProviderRegistry(IEnumerable<IAiProvider> providers, TimeProvider timeProvider, ILogger<AiProviderRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        this.logger = logger ?? NullLogger<AiProviderRegistry>.Instance;
        foreach (var provider in providers)
        {
            Register(provider);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IAiProvider> Providers => providers;

    /// <inheritdoc />
    public void Register(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (providersLock)
        {
            if (providers.Any(existing => string.Equals(existing.Id, provider.Id, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"A provider with the ID {provider.Id} is already registered.", nameof(provider));
            }

            providers = [.. providers, provider];
        }

        lastHealth.TryRemove(provider.Id, out _);
    }

    /// <inheritdoc />
    public bool Unregister(string providerId)
    {
        lock (providersLock)
        {
            var remaining = providers.Where(provider => !string.Equals(provider.Id, providerId, StringComparison.Ordinal)).ToList();
            if (remaining.Count == providers.Count)
            {
                return false;
            }

            providers = remaining;
        }

        lastHealth.TryRemove(providerId, out _);
        return true;
    }

    /// <inheritdoc />
    public IAiProvider? Find(string providerId) =>
        providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal));

    /// <inheritdoc />
    public IReadOnlyList<TCapability> GetProviders<TCapability>()
        where TCapability : class, IAiProvider =>
        providers.OfType<TCapability>().Where(provider => provider.IsSupportedOnCurrentPlatform).ToList();

    /// <inheritdoc />
    public ITextEmbeddingProvider? FindTextEmbedderFor(ModelIdentity model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return GetProviders<ITextEmbeddingProvider>().FirstOrDefault(provider => provider.Model == model);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderStatus>> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var snapshot = providers;
        return await Task.WhenAll(snapshot.Select(provider => CheckAsync(provider, cancellationToken))).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProviderStatus> CheckHealthAsync(string providerId, CancellationToken cancellationToken)
    {
        var provider = Find(providerId) ?? throw new KeyNotFoundException($"No provider has the ID {providerId}.");
        return CheckAsync(provider, cancellationToken);
    }

    /// <inheritdoc />
    public ProviderStatus? GetLastHealth(string providerId) =>
        lastHealth.TryGetValue(providerId, out var status) ? status : null;

    private async Task<ProviderStatus> CheckAsync(IAiProvider provider, CancellationToken cancellationToken)
    {
        ProviderHealth health;
        if (!provider.IsSupportedOnCurrentPlatform)
        {
            health = new ProviderHealth(ProviderHealthStatus.NotSupportedOnPlatform, $"{provider.DisplayName} doesn't run on this system.");
        }
        else
        {
            try
            {
                health = await provider.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Health check of AI provider {ProviderId} failed", provider.Id);
                health = new ProviderHealth(ProviderHealthStatus.Unhealthy, $"{provider.DisplayName} couldn't be checked.", ex.Message);
            }
        }

        if (!health.IsHealthy)
        {
            logger.LogInformation(
                "AI provider {ProviderId} is {Status}: {Message} {Detail}",
                provider.Id,
                health.Status,
                health.Message,
                health.Detail);
        }

        var status = new ProviderStatus(
            provider.Id,
            provider.DisplayName,
            provider.Kind,
            provider.Model,
            provider.GetCapabilities(),
            health,
            timeProvider.GetUtcNow());

        // Don't record a provider that was unregistered while it was being checked.
        if (Find(provider.Id) is not null)
        {
            lastHealth[provider.Id] = status;
        }

        return status;
    }
}
