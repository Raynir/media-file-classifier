namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// Adds a section to the navigation panel (E13-S02-T01). Each Epic registers one in its <c>Add…</c> module, e.g.
/// Favorites (E11-S01) or Collections (E11-S02), without changing the panel.
/// </summary>
public interface INavigationContributor
{
    /// <summary>
    /// Gets the section. It's created once; the contributor keeps its entries up to date in <see cref="RefreshAsync"/>.
    /// </summary>
    NavigationSection Section { get; }

    /// <summary>
    /// Updates the section's entries and counts. The panel calls it on the UI thread when a library opens and, at
    /// most once a second, while the library's items change (E13-S02-T04). Do the counting in queries, not here.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entries are up to date.</returns>
    Task RefreshAsync(CancellationToken cancellationToken);
}
