using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The file-system facts stored for an item, which a rescan compares with the file to skip unchanged files (E03-S02-T03).
/// </summary>
/// <param name="Path">Path relative to the item's folder.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="FileModifiedAt">File modification time, to the millisecond as stored.</param>
public sealed record MediaItemFileState(RelativePath Path, long SizeBytes, DateTimeOffset FileModifiedAt);
