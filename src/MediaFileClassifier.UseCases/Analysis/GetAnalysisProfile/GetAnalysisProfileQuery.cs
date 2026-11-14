using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;

/// <summary>
/// Gets what the analysis profile editor shows (E17-S02-T01): the open library's profile and, per analyzer, the
/// providers that can run it.
/// </summary>
/// <param name="CheckHealth">Whether to check the providers now (this reaches the network for an endpoint), or use the last result.</param>
public sealed record GetAnalysisProfileQuery(bool CheckHealth = false) : IRequest<AnalysisProfileEditorModel?>;

/// <summary>
/// The profile of the open library with the choices for each analyzer.
/// </summary>
/// <param name="Profile">The profile.</param>
/// <param name="Analyzers">One entry per analyzer the app knows, in running order.</param>
public sealed record AnalysisProfileEditorModel(AnalysisProfile Profile, IReadOnlyList<AnalyzerChoices> Analyzers);

/// <summary>
/// The providers that can run one analyzer.
/// </summary>
/// <param name="Analyzer">Analyzer key, see <see cref="AnalyzerKeys"/>.</param>
/// <param name="Providers">The providers registered for the analyzer's capability, including ones that don't run here.</param>
public sealed record AnalyzerChoices(string Analyzer, IReadOnlyList<ProviderChoice> Providers);

/// <summary>
/// One provider an analyzer can use.
/// </summary>
/// <param name="Id">Provider instance ID.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="ModelName">The model it runs.</param>
/// <param name="Health">Its last known health, or <c>null</c> when it wasn't checked yet.</param>
/// <param name="SupportedHere">Whether it runs on this platform.</param>
public sealed record ProviderChoice(string Id, string DisplayName, string ModelName, ProviderHealth? Health, bool SupportedHere);

/// <summary>
/// Handles <see cref="GetAnalysisProfileQuery"/>.
/// </summary>
/// <param name="session">The library session.</param>
/// <param name="registry">The provider registry.</param>
public sealed class GetAnalysisProfileQueryHandler(ILibrarySession session, IAiProviderRegistry registry)
    : IRequestHandler<GetAnalysisProfileQuery, AnalysisProfileEditorModel?>
{
    /// <inheritdoc />
    public async Task<AnalysisProfileEditorModel?> Handle(GetAnalysisProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var open = session.Current;
        if (open is null)
        {
            return null;
        }

        if (request.CheckHealth)
        {
            await registry.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        }

        // The profile lists the analyzers the library knows; one a profile lacks still shows, so it can be added.
        var profile = open.Library.Settings.AnalysisProfile;
        var keys = new[] { AnalyzerKeys.Embedding, AnalyzerKeys.Describer, AnalyzerKeys.Ocr };
        var analyzers = keys.Select(key => new AnalyzerChoices(key, ChoicesFor(key))).ToList();
        return new AnalysisProfileEditorModel(profile, analyzers);
    }

    private List<ProviderChoice> ChoicesFor(string key)
    {
        IEnumerable<IAiProvider> candidates = key switch
        {
            AnalyzerKeys.Embedding => registry.Providers.OfType<IImageEmbeddingProvider>(),
            AnalyzerKeys.Describer => registry.Providers.OfType<IImageDescriber>(),
            AnalyzerKeys.Ocr => registry.Providers.OfType<IOcrProvider>(),
            _ => [],
        };
        return [.. candidates.Select(provider => new ProviderChoice(
            provider.Id,
            provider.DisplayName,
            provider.Model.ModelName,
            registry.GetLastHealth(provider.Id)?.Health,
            provider.IsSupportedOnCurrentPlatform))];
    }
}
