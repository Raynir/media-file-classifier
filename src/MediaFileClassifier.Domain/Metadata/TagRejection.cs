using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Tombstone for a tag the user removed or rejected. While it exists, AI and technical assignments of the tag
/// are ignored, so re-analysis can't add it again (D020). One per item and tag.
/// </summary>
/// <param name="ItemId">Item the tag was rejected on.</param>
/// <param name="Tag">The rejected tag.</param>
/// <param name="RejectedFrom">AI run that suggested the tag, if it was an AI tag.</param>
/// <param name="RejectedAt">When the user rejected it.</param>
public sealed record TagRejection(Id<MediaItem> ItemId, TagName Tag, RunReference? RejectedFrom, DateTimeOffset RejectedAt);
