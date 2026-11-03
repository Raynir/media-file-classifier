using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;
using MediatR;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Opens an item when the user double-clicks its tile or presses Enter (E14-S01). The preview (E14-S05) and the
/// full-screen viewer (E14-S08) replace the M1 implementation, so the grid doesn't change.
/// </summary>
public interface IItemOpener
{
    /// <summary>
    /// Opens an item.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <returns><see langword="true"/> if it opened.</returns>
    Task<bool> OpenAsync(Id<MediaItem> itemId);
}

/// <summary>
/// Opens an item's file in the system's default app, until the preview exists (E14-S01). Uses Avalonia's launcher,
/// which works on every platform head.
/// </summary>
/// <param name="sender">Sends the path query.</param>
public sealed class DefaultAppItemOpener(ISender sender) : IItemOpener
{
    /// <inheritdoc />
    public async Task<bool> OpenAsync(Id<MediaItem> itemId)
    {
        var path = await sender.Send(new GetItemFilePathQuery(itemId));
        var topLevel = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null,
        };
        return path is not null && File.Exists(path) && topLevel is not null && await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }
}
