using MediaFileClassifier.Ui.Toolbar;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// Puts the search box in the toolbar's Search slot (E13-S03, E09-S01-T05).
/// </summary>
/// <param name="searchBar">The search box.</param>
public sealed class SearchToolbar(SearchBarViewModel searchBar) : IToolbarContributor
{
    /// <inheritdoc />
    public void Register(ToolbarViewModel toolbar) => toolbar.Search = searchBar;
}
