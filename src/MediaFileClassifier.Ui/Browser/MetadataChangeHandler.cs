using Avalonia.Threading;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.UseCases.Metadata;
using MediatR;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Updates the open views when user values change (D370): the grid reloads at once; for favorites (E11-S01-T01) the
/// Favorite command reads the selection's state again, and for fields and tags (E07-S01-T03) the details panel reloads
/// the item it shows, if it changed. The navigation panel's counts follow the library's own change event.
/// </summary>
/// <param name="grid">The thumbnail grid.</param>
/// <param name="commands">The Favorite command.</param>
/// <param name="details">The details panel, resolved when a change arrives.</param>
public sealed class MetadataChangeHandler(Func<ThumbnailGridViewModel> grid, FavoriteCommands commands, Func<DetailsPanelViewModel> details)
    : INotificationHandler<ItemsMetadataChanged>
{
    /// <inheritdoc />
    public Task Handle(ItemsMetadataChanged notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Handlers run on the writing thread; the views are on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (notification.Change.HasFlag(ItemMetadataChange.Favorite))
            {
                commands.Reload();
            }

            if (notification.Change.HasFlag(ItemMetadataChange.Fields) || notification.Change.HasFlag(ItemMetadataChange.Tags))
            {
                details().Reload(notification.ItemIds);
            }

            _ = grid().RefreshNowAsync();
        });

        return Task.CompletedTask;
    }
}
