using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Duplicates.GetDuplicateStatistics;

/// <summary>
/// Gets the number of Open duplicate groups, of every type, and the space their extra copies take (E12-S05-T01): the
/// library information window, the Duplicates view and the dashboard (E15-S05) all show these figures. The wasted
/// space is, per group, the summed size of its members minus its largest member. Without an open library the
/// result is <see cref="DuplicateStatistics.None"/>.
/// </summary>
public sealed record GetDuplicateStatisticsQuery : IRequest<DuplicateStatistics>;

/// <summary>
/// Handles <see cref="GetDuplicateStatisticsQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetDuplicateStatisticsQueryHandler(ILibrarySession session) : IRequestHandler<GetDuplicateStatisticsQuery, DuplicateStatistics>
{
    /// <inheritdoc />
    public async Task<DuplicateStatistics> Handle(GetDuplicateStatisticsQuery request, CancellationToken cancellationToken) =>
        session.Current is { } library
            ? await library.Duplicates.GetOpenStatisticsAsync(cancellationToken).ConfigureAwait(false)
            : DuplicateStatistics.None;
}
