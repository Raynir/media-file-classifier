using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;

namespace MediaFileClassifier.UseCases.Analysis.Outdated;

/// <summary>
/// Resolves <see cref="CurrentProvenance"/> for the open library's analysis profile.
/// </summary>
/// <param name="session">The library session.</param>
/// <param name="registry">The AI providers.</param>
public sealed class AnalysisProvenanceSource(ILibrarySession session, IAiProviderRegistry registry) : IAnalysisProvenanceSource
{
    /// <inheritdoc />
    public IReadOnlyList<AnalyzerProvenance> GetCurrent() =>
        session.Current is { } library ? CurrentProvenance.Resolve(library.Library.Settings.AnalysisProfile, registry) : [];
}
