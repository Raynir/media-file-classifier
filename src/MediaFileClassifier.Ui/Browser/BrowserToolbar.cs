using MediaFileClassifier.Ui.Toolbar;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Puts the media browser's view controls in the toolbar's View slot (E13-S03, E14-S01-T05): the thumbnail size
/// slider for now, and the grid/list toggle with E14-S02.
/// </summary>
/// <param name="grid">Gets the thumbnail grid.</param>
public sealed class BrowserToolbar(Func<ThumbnailGridViewModel> grid) : IToolbarContributor
{
    /// <inheritdoc />
    public void Register(ToolbarViewModel toolbar) => toolbar.View = new ThumbnailSizeViewModel(grid);
}
