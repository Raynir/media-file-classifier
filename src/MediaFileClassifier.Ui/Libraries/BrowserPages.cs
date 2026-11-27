using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.ViewModels.Duplicates;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// Swaps the main content area between the media browser and pages that take its place, such as Duplicates
/// (E12-S05-T03). The navigation panel picks the page; picking a scope entry, or opening another library, brings the
/// browser back.
/// </summary>
/// <param name="regions">The shell's regions.</param>
/// <param name="duplicates">The Duplicates view model.</param>
/// <param name="grid">Gets the media browser.</param>
public sealed class BrowserPages(ShellRegions regions, DuplicatesViewModel duplicates, Func<ThumbnailGridViewModel> grid)
{
    private ViewModelBase? replaced;

    /// <summary>
    /// Gets a value indicating whether the Duplicates view is in the main content area.
    /// </summary>
    public bool IsDuplicatesShown => ReferenceEquals(regions.Browser, duplicates);

    /// <summary>
    /// Shows the Duplicates view in place of the media browser, and loads it.
    /// </summary>
    public void ShowDuplicates()
    {
        if (!IsDuplicatesShown)
        {
            replaced = regions.Browser;
            regions.Browser = duplicates;
        }

        _ = duplicates.ActivateAsync();
    }

    /// <summary>
    /// Puts the media browser back if a page took its place.
    /// </summary>
    public void ShowBrowser()
    {
        if (!IsDuplicatesShown)
        {
            return;
        }

        duplicates.Deactivate();
        regions.Browser = replaced ?? grid();
        replaced = null;
    }
}
