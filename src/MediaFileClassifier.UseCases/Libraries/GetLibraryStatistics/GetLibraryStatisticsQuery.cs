using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;

/// <summary>
/// Counts the open library's items by media type and status (E02-S05-T01), or returns <see langword="null"/> when no
/// library is open. One aggregate query, so it's fast enough to run during an import (under 100 ms at 100k items).
/// </summary>
public sealed record GetLibraryStatisticsQuery : IRequest<LibraryStatistics?>;

/// <summary>
/// Handles <see cref="GetLibraryStatisticsQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetLibraryStatisticsQueryHandler(ILibrarySession session) : IRequestHandler<GetLibraryStatisticsQuery, LibraryStatistics?>
{
    /// <summary>
    /// Builds the statistics from the per-type-and-status counts.
    /// </summary>
    /// <param name="counts">The counts.</param>
    /// <returns>The statistics.</returns>
    public static LibraryStatistics Summarize(IEnumerable<ItemStatisticsCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var rows = counts.Where(row => row.Status != MediaItemStatus.Excluded).ToList();
        int Count(Func<ItemStatisticsCount, bool> match) => rows.Where(match).Sum(row => row.Count);

        return new LibraryStatistics(
            Images: Count(row => row.MediaType == MediaType.Image),
            AnimatedImages: Count(row => row.MediaType == MediaType.AnimatedImage),
            Videos: Count(row => row.MediaType == MediaType.Video),
            Indexed: Count(row => row.Status == MediaItemStatus.Indexed),
            Pending: Count(row => row.Status is MediaItemStatus.Discovered or MediaItemStatus.Pending),
            Errors: Count(row => row.Status == MediaItemStatus.Error),
            Missing: Count(row => row.Status == MediaItemStatus.Missing),
            MediaSizeBytes: rows.Where(row => row.Status != MediaItemStatus.Missing).Sum(row => row.SizeBytes));
    }

    /// <inheritdoc />
    public async Task<LibraryStatistics?> Handle(GetLibraryStatisticsQuery request, CancellationToken cancellationToken)
    {
        if (session.Current is not { } library)
        {
            return null;
        }

        return Summarize(await library.Items.CountByTypeAndStatusAsync(cancellationToken).ConfigureAwait(false));
    }
}
