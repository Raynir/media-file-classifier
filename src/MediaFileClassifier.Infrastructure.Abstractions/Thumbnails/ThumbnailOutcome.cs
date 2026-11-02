using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// What generating an item's thumbnails left in the cache.
/// </summary>
/// <param name="IsStored">Whether thumbnails were written. Without them the item shows a placeholder.</param>
/// <param name="Warnings">Problems to store with the item's technical metadata warnings.</param>
public sealed record ThumbnailOutcome(bool IsStored, IReadOnlyList<ExtractionWarning> Warnings);
