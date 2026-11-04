using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Opens an item when the user double-clicks its tile or presses Enter (E14-S01). The details panel (E14-S05) shows
/// it; the full-screen viewer (E14-S08) can replace that without changing the grid.
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
