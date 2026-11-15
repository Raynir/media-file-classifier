using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.Outdated;

/// <summary>
/// Finds the items whose analysis is outdated (E06-S07-T01): results that came from another provider, model or prompt
/// version than the profile's analyzers use now (<see cref="CurrentProvenance"/>).
/// </summary>
/// <param name="Profile">
/// The profile to compare with, e.g. one the settings page is about to save, to show how many items a change makes
/// outdated; <see langword="null"/> for the open library's profile.
/// </param>
public sealed record GetOutdatedAnalysisQuery(AnalysisProfile? Profile = null) : IRequest<OutdatedAnalysis>;

/// <summary>
/// Handles <see cref="GetOutdatedAnalysisQuery"/>.
/// </summary>
/// <param name="session">The library session.</param>
/// <param name="registry">The provider registry.</param>
public sealed class GetOutdatedAnalysisQueryHandler(ILibrarySession session, IAiProviderRegistry registry)
    : IRequestHandler<GetOutdatedAnalysisQuery, OutdatedAnalysis>
{
    /// <inheritdoc />
    /// <returns>The outdated items, or none when no library is open.</returns>
    public async Task<OutdatedAnalysis> Handle(GetOutdatedAnalysisQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (session.Current is not { } library)
        {
            return OutdatedAnalysis.None;
        }

        var current = CurrentProvenance.Resolve(request.Profile ?? library.Library.Settings.AnalysisProfile, registry);
        return await library.AnalyzerStates.GetOutdatedAsync(current, cancellationToken).ConfigureAwait(false);
    }
}
