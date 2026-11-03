using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediatR;

namespace MediaFileClassifier.UseCases.Import.RetryFailedItems;

/// <summary>
/// "Retry failed items" (E03-S07-T02): queues the failed stage again for items that failed during an import. A
/// permanent failure, such as a damaged file, is retried only once the file has changed, and then from the start.
/// </summary>
/// <param name="JobId">Retry the items of this job's folder, or <c>null</c> for every failed item in the library.</param>
public sealed record RetryFailedItemsCommand(Id<ProcessingJob>? JobId = null) : IRequest<RetryFailedItemsResult>;

/// <summary>
/// What <see cref="RetryFailedItemsCommand"/> did.
/// </summary>
/// <param name="Retried">Items queued again.</param>
/// <param name="Unchanged">Items with a permanent failure whose file hasn't changed, left as they are.</param>
public sealed record RetryFailedItemsResult(int Retried, int Unchanged);
