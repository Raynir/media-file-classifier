using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Items with the same full content hash: exact copies of one file at different paths (E03-S07-T06).
/// </summary>
/// <param name="ContentHash">The shared content hash.</param>
/// <param name="ItemIds">The items, two or more, in ID order.</param>
public sealed record ExactDuplicateGroup(UInt128 ContentHash, IReadOnlyList<Id<MediaItem>> ItemIds);
