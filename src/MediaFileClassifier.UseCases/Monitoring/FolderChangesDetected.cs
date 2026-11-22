using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Published when files of a watched folder changed and are stable (E04-S01): the seam where change processing
/// (E04-S02) takes over. Changes that the ignore list, the folder's rules or its partial-folder mode leave out, and
/// changes to excluded items, aren't in it. Nothing handles it yet, so nothing is imported from it.
/// </summary>
/// <remarks>Handlers run on a thread-pool thread, one batch at a time, so UI handlers dispatch to the UI thread themselves.</remarks>
/// <param name="FolderId">The folder.</param>
/// <param name="Changes">The changes, in the order they became stable.</param>
public sealed record FolderChangesDetected(Id<IndexedFolder> FolderId, IReadOnlyList<FolderChange> Changes) : INotification;
