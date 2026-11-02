using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The facts of a media item that file matching compares in memory (E01-S06-T05): the perceptual hash and the
/// file name heuristic. The exact tiers look items up by index instead.
/// </summary>
/// <param name="Id">Item ID.</param>
/// <param name="FileName">File name with extension.</param>
/// <param name="MediaType">Kind of media.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="FileModifiedAt">File modification time.</param>
/// <param name="PerceptualHash">Perceptual hash, if computed.</param>
public sealed record MediaItemIdentity(
    Id<MediaItem> Id,
    string FileName,
    MediaType MediaType,
    long SizeBytes,
    DateTimeOffset FileModifiedAt,
    ulong? PerceptualHash);
