using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Where an item is, and its status (E14-S04).
/// </summary>
/// <param name="Id">The item.</param>
/// <param name="FolderId">The indexed folder it's in.</param>
/// <param name="Path">Its path within the folder.</param>
/// <param name="Status">Its status, e.g. Missing.</param>
public sealed record MediaItemPath(Id<MediaItem> Id, Id<IndexedFolder> FolderId, RelativePath Path, MediaItemStatus Status);
