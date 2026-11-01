using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.AddLibraryFolders;

/// <summary>
/// Adds folders to the open library, as the last step of creating it (E02-S01-T03). Folders already in the library
/// are skipped. Scanning them comes with E02-S03 and E03.
/// </summary>
/// <param name="Folders">Folders the user picked.</param>
public sealed record AddLibraryFoldersCommand(IReadOnlyList<FolderHandle> Folders) : IRequest<IReadOnlyList<IndexedFolder>>;

/// <summary>
/// Handles <see cref="AddLibraryFoldersCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class AddLibraryFoldersCommandHandler(ILibrarySession session, TimeProvider timeProvider)
    : IRequestHandler<AddLibraryFoldersCommand, IReadOnlyList<IndexedFolder>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexedFolder>> Handle(AddLibraryFoldersCommand request, CancellationToken cancellationToken)
    {
        var library = session.Current ?? throw new InvalidOperationException("No library is open.");
        var existing = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(folder => folder.PlatformHandle)
            .ToHashSet(StringComparer.Ordinal);
        var added = new List<IndexedFolder>();
        foreach (var handle in request.Folders.Where(handle => existing.Add(handle.Id)))
        {
            // On the desktop the handle is a full path, which is the clearer thing to show.
            var displayPath = Path.IsPathFullyQualified(handle.Id) ? handle.Id : handle.DisplayName;
            var folder = IndexedFolder.Create(library.Library.Id, displayPath, handle.Id, timeProvider.GetUtcNow());
            await library.Folders.AddAsync(folder, cancellationToken).ConfigureAwait(false);
            added.Add(folder);
        }

        return added;
    }
}
