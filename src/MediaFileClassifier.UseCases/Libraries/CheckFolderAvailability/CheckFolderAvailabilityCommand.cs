using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Checks whether the open library's folders can be reached, and marks them Online or Offline (E02-S03-T05). It runs
/// when a library opens, before imports and when the folders view opens. An Offline folder's items are kept as they
/// are: nothing is marked Missing because its root can't be reached.
/// </summary>
/// <param name="FolderIds">The folders to check, or <c>null</c> for all of them.</param>
public sealed record CheckFolderAvailabilityCommand(IReadOnlyCollection<Id<IndexedFolder>>? FolderIds = null) : IRequest<IReadOnlyList<IndexedFolder>>;

/// <summary>
/// Handles <see cref="CheckFolderAvailabilityCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Checks the folders.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class CheckFolderAvailabilityCommandHandler(
    ILibrarySession session,
    IMediaFileSource files,
    TimeProvider clock,
    ILogger<CheckFolderAvailabilityCommandHandler> logger) : IRequestHandler<CheckFolderAvailabilityCommand, IReadOnlyList<IndexedFolder>>
{
    /// <inheritdoc />
    /// <returns>The folders whose status changed.</returns>
    public async Task<IReadOnlyList<IndexedFolder>> Handle(CheckFolderAvailabilityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return [];
        }

        // Checked at once, so unreachable network shares time out together rather than one after another.
        var folders = (await library.Folders.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(folder => request.FolderIds is null || request.FolderIds.Contains(folder.Id))
            .ToList();
        var available = await Task.WhenAll(folders.Select(folder => files.IsAvailableAsync(folder.ToHandle(), cancellationToken))).ConfigureAwait(false);
        var changed = new List<IndexedFolder>();
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            var status = available[i] ? FolderStatus.Online : FolderStatus.Offline;
            if (folder.Status == status)
            {
                continue;
            }

            if (available[i])
            {
                folder.MarkOnline(clock.GetUtcNow());
            }
            else
            {
                folder.MarkOffline(clock.GetUtcNow());
            }

            await library.Folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Folder {Folder} is {Status} now", folder.DisplayPath, status);
            changed.Add(folder);
        }

        return changed;
    }
}
