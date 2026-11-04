namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// Why an item is in <see cref="MediaItemStatus.Error"/>: the processing stage that failed and what went wrong
/// (E03-S07-T02). The message is the item's <see cref="MediaItem.StatusReason"/>.
/// </summary>
/// <param name="Stage">Key of the stage that failed, e.g. <c>import.media</c>.</param>
/// <param name="ExceptionType">Name of the exception's type, or <c>null</c> when the stage found the problem itself.</param>
/// <param name="IsPermanent">
/// Whether the file itself is the problem, e.g. it can't be decoded. A retry skips such an item until the file changes.
/// </param>
public sealed record MediaItemError(string Stage, string? ExceptionType, bool IsPermanent);
