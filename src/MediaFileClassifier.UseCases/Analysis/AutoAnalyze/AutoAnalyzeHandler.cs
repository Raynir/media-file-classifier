using MediaFileClassifier.UseCases.Import;
using MediatR;

namespace MediaFileClassifier.UseCases.Analysis.AutoAnalyze;

/// <summary>
/// Hands every registered item to the <see cref="AutoAnalyzeBatcher"/> (E06-S06-T01). A re-registered item whose
/// content changed is analyzed again the same way.
/// </summary>
/// <param name="batcher">Collects the items of a burst.</param>
public sealed class AutoAnalyzeHandler(AutoAnalyzeBatcher batcher) : INotificationHandler<MediaItemRegistered>
{
    /// <inheritdoc />
    public Task Handle(MediaItemRegistered notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        batcher.Add(notification.ItemId);
        return Task.CompletedTask;
    }
}
