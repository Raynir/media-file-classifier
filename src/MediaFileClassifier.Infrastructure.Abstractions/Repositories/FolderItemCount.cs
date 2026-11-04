using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// How many items of a folder have a status.
/// </summary>
/// <param name="FolderId">The folder.</param>
/// <param name="Status">The status.</param>
/// <param name="Count">The number of items.</param>
public sealed record FolderItemCount(Id<IndexedFolder> FolderId, MediaItemStatus Status, int Count);
