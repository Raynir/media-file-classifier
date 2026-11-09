using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediatR;

namespace MediaFileClassifier.UseCases.Import.RetryFailedItems;

/// <summary>
/// "Retry failed items" (E03-S07-T02): queues the failed stage again for items that failed during an import. A
/// permanent failure, such as a damaged file, is retried only once the file has changed, and then from the start.
/// </summary>
/// <remarks>
/// The three manual retry entry points (E05-S04-T04): per job, per selected items (the context menu and the Item
/// menu), and for the whole library (the Library menu).
/// </remarks>
/// <param name="JobId">Retry the items of this job's folder, or <c>null</c> for every failed item in the library.</param>
/// <param name="ItemIds">Retry only these items, or <c>null</c> for every failed item the job or library has.</param>
public sealed record RetryFailedItemsCommand(Id<ProcessingJob>? JobId = null, IReadOnlyCollection<Id<MediaItem>>? ItemIds = null) : IRequest<RetryFailedItemsResult>;

/// <summary>
/// What <see cref="RetryFailedItemsCommand"/> did.
/// </summary>
/// <param name="Retried">Items queued again.</param>
/// <param name="Unchanged">Items with a permanent failure whose file hasn't changed, left as they are.</param>
public sealed record RetryFailedItemsResult(int Retried, int Unchanged);
