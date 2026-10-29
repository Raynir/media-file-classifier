using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Collections;

/// <summary>
/// An item in a collection. An item can be in many collections, and appears in each one at most once.
/// </summary>
/// <param name="CollectionId">The collection.</param>
/// <param name="ItemId">The item.</param>
/// <param name="AddedAt">When the item was added.</param>
public sealed record CollectionMembership(Id<Collection> CollectionId, Id<MediaItem> ItemId, DateTimeOffset AddedAt);
