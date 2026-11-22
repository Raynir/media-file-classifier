using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Hands folders that came back Online to the <see cref="ReconnectCatchUp"/> (E04-S04-T02). It returns at once, so the
/// availability check that published the notification doesn't wait for the syncs.
/// </summary>
/// <param name="catchUp">Runs the catch-ups.</param>
public sealed class FoldersReconnectedHandler(ReconnectCatchUp catchUp) : INotificationHandler<FoldersReconnected>
{
    /// <inheritdoc />
    public Task Handle(FoldersReconnected notification, CancellationToken cancellationToken)
    {
        catchUp.Start(notification.FolderIds);
        return Task.CompletedTask;
    }
}
