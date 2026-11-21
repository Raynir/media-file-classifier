using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// The number and the summed file size of a library's items of one media type and status (E02-S05-T01).
/// </summary>
/// <param name="MediaType">The media type.</param>
/// <param name="Status">The status.</param>
/// <param name="Count">The number of items.</param>
/// <param name="SizeBytes">The summed file size of those items, in bytes.</param>
public sealed record ItemStatisticsCount(MediaType MediaType, MediaItemStatus Status, int Count, long SizeBytes);
