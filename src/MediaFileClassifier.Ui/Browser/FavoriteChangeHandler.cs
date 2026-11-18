using Avalonia.Threading;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Favorites;
using MediatR;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Updates the open views when favorites change (E11-S01-T01): the grid reloads at once, and the Favorite command
/// reads the selection's state again. The navigation panel's counts follow the library's own change event.
/// </summary>
/// <param name="grid">The thumbnail grid.</param>
/// <param name="commands">The Favorite command.</param>
public sealed class FavoriteChangeHandler(Func<ThumbnailGridViewModel> grid, FavoriteCommands commands)
    : INotificationHandler<ItemsMetadataChanged>
{
    /// <inheritdoc />
    public Task Handle(ItemsMetadataChanged notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Change == ItemMetadataChange.Favorite)
        {
            // Handlers run on the writing thread; the views are on the UI thread.
            Dispatcher.UIThread.Post(() =>
            {
                commands.Reload();
                _ = grid().RefreshNowAsync();
            });
        }

        return Task.CompletedTask;
    }
}
