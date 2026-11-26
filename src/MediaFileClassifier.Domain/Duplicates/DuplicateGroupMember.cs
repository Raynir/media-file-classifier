using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// An item in a <see cref="DuplicateGroup"/>. Once the changed hashes are reconciled, an item is in at most one group
/// of each type; while an item that got a new hash waits for its old hash's turn, it can be in both groups.
/// </summary>
/// <param name="GroupId">The group.</param>
/// <param name="ItemId">The item.</param>
/// <param name="JoinedAt">When the item joined the group.</param>
public sealed record DuplicateGroupMember(Id<DuplicateGroup> GroupId, Id<MediaItem> ItemId, DateTimeOffset JoinedAt);
