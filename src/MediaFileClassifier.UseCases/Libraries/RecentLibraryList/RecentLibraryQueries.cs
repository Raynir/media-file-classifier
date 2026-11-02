using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.RecentLibraryList;

/// <summary>
/// Lists the recent libraries, most recent first, with whether each can be opened now (E02-S02-T02).
/// </summary>
public sealed record GetRecentLibrariesQuery : IRequest<IReadOnlyList<RecentLibraryItem>>;

/// <summary>
/// A recent library and whether it's reachable.
/// </summary>
/// <param name="Library">The list entry.</param>
/// <param name="IsAvailable">Whether its folder and database exist, e.g. its drive is connected.</param>
public sealed record RecentLibraryItem(RecentLibrary Library, bool IsAvailable);

/// <summary>
/// Removes a library from the recent list. The library itself isn't touched.
/// </summary>
/// <param name="LibraryDirectory">The library folder.</param>
public sealed record RemoveRecentLibraryCommand(string LibraryDirectory) : IRequest;

/// <summary>
/// Handles <see cref="GetRecentLibrariesQuery"/> and <see cref="RemoveRecentLibraryCommand"/>.
/// </summary>
/// <param name="settings">App settings, with the list.</param>
/// <param name="locations">Checks whether libraries are reachable.</param>
public sealed class RecentLibraryHandlers(ISettingsService settings, ILibraryLocations locations)
    : IRequestHandler<GetRecentLibrariesQuery, IReadOnlyList<RecentLibraryItem>>, IRequestHandler<RemoveRecentLibraryCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RecentLibraryItem>> Handle(GetRecentLibrariesQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RecentLibraryItem>>(
            [.. (settings.Current.RecentLibraries ?? []).Select(entry => new RecentLibraryItem(entry, locations.IsAvailable(entry.Path)))]);

    /// <inheritdoc />
    public Task Handle(RemoveRecentLibraryCommand request, CancellationToken cancellationToken)
    {
        RecentLibraries.Remove(settings, request.LibraryDirectory);
        return Task.CompletedTask;
    }
}
