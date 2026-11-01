using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// An item a file matched.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Distance">The perceptual hash distance, for a perceptual match.</param>
public sealed record MatchedItem(Id<MediaItem> ItemId, int? Distance = null);
