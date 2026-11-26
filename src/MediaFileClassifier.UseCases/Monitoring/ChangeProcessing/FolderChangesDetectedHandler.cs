using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// Hands the watcher's changes (E04-S01) to change processing (E04-S02, D514).
/// </summary>
/// <param name="processor">Queues the changes.</param>
public sealed class FolderChangesDetectedHandler(ChangeProcessor processor) : INotificationHandler<FolderChangesDetected>
{
    /// <inheritdoc />
    public Task Handle(FolderChangesDetected notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return processor.SubmitAsync(notification.FolderId, notification.Changes, cancellationToken);
    }
}
