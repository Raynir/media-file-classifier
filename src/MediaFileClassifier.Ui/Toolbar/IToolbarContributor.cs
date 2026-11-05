namespace MediaFileClassifier.Ui.Toolbar;

/// <summary>
/// Adds one feature's controls and actions to the toolbar (E13-S03): it fills a slot, such as the search bar
/// (E09-S01), or adds an action button bound to a command, such as Analyze pending (E06-S06). Register an
/// implementation with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IToolbarContributor, MyToolbarItems&gt;())</c>.
/// </summary>
public interface IToolbarContributor
{
    /// <summary>
    /// Fills the feature's slots and adds its actions.
    /// </summary>
    /// <param name="toolbar">The toolbar.</param>
    void Register(ToolbarViewModel toolbar);
}
