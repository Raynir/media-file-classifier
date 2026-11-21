using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.GetLibraryDiskUsage;

/// <summary>
/// Gets the open library's disk usage (E02-S05-T03), or <see langword="null"/> when no library is open. A value
/// measured less than <paramref name="MaxAge"/> ago comes from the cache.
/// </summary>
/// <param name="MaxAge">How old a cached value may be.</param>
public sealed record GetLibraryDiskUsageQuery(TimeSpan MaxAge) : IRequest<LibraryDiskUsage?>;

/// <summary>
/// Handles <see cref="GetLibraryDiskUsageQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="diskUsage">Measures and caches the sizes.</param>
public sealed class GetLibraryDiskUsageQueryHandler(ILibrarySession session, ILibraryDiskUsage diskUsage) : IRequestHandler<GetLibraryDiskUsageQuery, LibraryDiskUsage?>
{
    /// <inheritdoc />
    public async Task<LibraryDiskUsage?> Handle(GetLibraryDiskUsageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return session.Current is { } library
            ? await diskUsage.MeasureAsync(library.Layout, request.MaxAge, cancellationToken).ConfigureAwait(false)
            : null;
    }
}
