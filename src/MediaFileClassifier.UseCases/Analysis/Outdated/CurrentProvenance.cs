using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.UseCases.Analysis.Pipeline;

namespace MediaFileClassifier.UseCases.Analysis.Outdated;

/// <summary>
/// Works out what each analyzer of a profile uses now (E06-S07-T01): the provider the next analysis would pick, as
/// <see cref="ProviderSelection"/> picks it, its model, and the prompt version the analyzer stores.
/// </summary>
public static class CurrentProvenance
{
    /// <summary>
    /// Resolves a profile. A disabled analyzer, or one without a provider that runs here, is left out, so its results
    /// are never outdated: analyzing again wouldn't change them.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="registry">The provider registry.</param>
    /// <returns>One provenance per analyzer that can run, in the profile's order.</returns>
    public static IReadOnlyList<AnalyzerProvenance> Resolve(AnalysisProfile profile, IAiProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registry);

        var result = new List<AnalyzerProvenance>();
        foreach (var settings in profile.Analyzers.Where(settings => settings.Enabled))
        {
            // The circuit breaker is ignored: a paused provider is still the one the analysis waits for.
            IAiProvider? provider = settings.Analyzer switch
            {
                AnalyzerKeys.Embedding => ProviderSelection.Select<IImageEmbeddingProvider>(registry, settings, null, out _, out _),
                AnalyzerKeys.Describer => ProviderSelection.Select<IImageDescriber>(registry, settings, null, out _, out _),
                AnalyzerKeys.Ocr => ProviderSelection.Select<IOcrProvider>(registry, settings, null, out _, out _),
                _ => null,
            };
            if (provider is null)
            {
                continue;
            }

            // The describer stores its provider's prompt version; OCR and the embedding store none.
            var promptVersion = (provider as IImageDescriber)?.PromptVersion;
            var model = provider.Model;
            result.Add(new AnalyzerProvenance(settings.Analyzer, model.ProviderId, model.ModelName, model.Version, promptVersion));
        }

        return result;
    }
}
