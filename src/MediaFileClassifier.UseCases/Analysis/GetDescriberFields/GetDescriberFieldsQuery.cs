using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Analysis.Pipeline;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.GetDescriberFields;

/// <summary>
/// Gets the fields the open library's describer can produce now (E06-S04-T03): the describer the next analysis would
/// pick, as the pipeline picks it. The editor shows the other fields as not available with the current model.
/// </summary>
public sealed record GetDescriberFieldsQuery : IRequest<DescriberFields?>;

/// <summary>
/// Handles <see cref="GetDescriberFieldsQuery"/>.
/// </summary>
/// <param name="session">The library session.</param>
/// <param name="registry">The provider registry.</param>
public sealed class GetDescriberFieldsQueryHandler(ILibrarySession session, IAiProviderRegistry registry)
    : IRequestHandler<GetDescriberFieldsQuery, DescriberFields?>
{
    /// <inheritdoc />
    /// <returns>The fields, or <c>null</c> when no library is open, the describer is off or no describer runs here.</returns>
    public Task<DescriberFields?> Handle(GetDescriberFieldsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current?.Library.Settings.AnalysisProfile.Find(AnalyzerKeys.Describer) is not { Enabled: true } settings)
        {
            return Task.FromResult<DescriberFields?>(null);
        }

        // The circuit breaker is ignored: a paused describer is still the one the next analysis waits for.
        var provider = ProviderSelection.Select<IImageDescriber>(registry, settings, null, out _, out _);
        return Task.FromResult<DescriberFields?>(provider?.SupportedFields);
    }
}
