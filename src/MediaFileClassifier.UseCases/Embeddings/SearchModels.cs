using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Analysis.Pipeline;

namespace MediaFileClassifier.UseCases.Embeddings;

/// <summary>
/// Which embedding model search uses and which one analysis uses (E06-S07-T05). Search uses the library's active
/// search model (<see cref="LibrarySettings.ActiveSearchModelId"/>) while a provider for it is registered; analysis
/// embeds with the current model, the one <see cref="ProviderSelection"/> picks for the profile. They differ during a
/// model migration, until <see cref="EmbeddingModelMigration"/> switches search over.
/// </summary>
public static class SearchModels
{
    /// <summary>
    /// Checks whether a registration describes a provider's model. The version is compared only when both have one,
    /// and the file hash not at all, as outdated detection compares them (E06-S07-T01).
    /// </summary>
    /// <param name="model">The provider's model.</param>
    /// <param name="registration">The registration.</param>
    /// <returns><see langword="true"/> if it's the same model.</returns>
    public static bool Matches(ModelIdentity model, ModelRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(registration);

        return string.Equals(model.ProviderId, registration.ProviderId, StringComparison.Ordinal)
            && string.Equals(model.ModelName, registration.ModelName, StringComparison.Ordinal)
            && (model.Version is null || registration.Version is null || string.Equals(model.Version, registration.Version, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the provider analysis embeds with now: the profile's embedding provider and model, as the analysis picks it.
    /// </summary>
    /// <param name="registry">The provider registry.</param>
    /// <param name="settings">The library settings.</param>
    /// <returns>The provider, or <see langword="null"/> when the embedding analyzer is off or has no provider here.</returns>
    public static IImageEmbeddingProvider? FindCurrentEmbedder(IAiProviderRegistry registry, LibrarySettings settings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);

        var analyzer = settings.AnalysisProfile.Find(AnalyzerKeys.Embedding);
        if (analyzer is { Enabled: false })
        {
            return null;
        }

        return ProviderSelection.Select<IImageEmbeddingProvider>(registry, analyzer ?? new AnalyzerSettings(AnalyzerKeys.Embedding), null, out _, out _);
    }

    /// <summary>
    /// Finds the provider that encodes search queries for a library: one of the active search model while there is
    /// one, else one of the current embedding model, else the first.
    /// </summary>
    /// <typeparam name="TProvider">The capability, a text or image embedder.</typeparam>
    /// <param name="registry">The provider registry.</param>
    /// <param name="library">The open library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provider, or <see langword="null"/> when none is registered.</returns>
    public static async Task<TProvider?> FindSearchProviderAsync<TProvider>(IAiProviderRegistry registry, IOpenLibrary library, CancellationToken cancellationToken)
        where TProvider : class, IAiProvider
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(library);

        var providers = registry.GetProviders<TProvider>();
        if (providers.Count == 0)
        {
            return null;
        }

        var settings = library.Library.Settings;
        if (settings.ActiveSearchModelId is { } activeId)
        {
            var models = await library.Models.GetAllAsync(cancellationToken).ConfigureAwait(false);
            if (models.FirstOrDefault(model => model.Id == activeId) is { } active
                && providers.FirstOrDefault(provider => Matches(provider.Model, active)) is { } forActive)
            {
                return forActive;
            }
        }

        // No active model yet, or its provider is gone: search uses the current model (D051 for the rest).
        var current = FindCurrentEmbedder(registry, settings)?.Model;
        return providers.FirstOrDefault(provider => current is not null && SameModel(provider.Model, current)) ?? providers[0];
    }

    private static bool SameModel(ModelIdentity left, ModelIdentity right) =>
        string.Equals(left.ProviderId, right.ProviderId, StringComparison.Ordinal)
        && string.Equals(left.ModelName, right.ModelName, StringComparison.Ordinal)
        && (left.Version is null || right.Version is null || string.Equals(left.Version, right.Version, StringComparison.Ordinal));
}
