namespace MediaFileClassifier.Ui.StatusBar;

/// <summary>
/// Adds one feature's segments to the status bar (E13-S04-T01), e.g. the processing indicator (E05-S02).
/// Register an implementation with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IStatusBarContributor, MySegments&gt;())</c>,
/// then keep the segments' text and visibility up to date.
/// </summary>
public interface IStatusBarContributor
{
    /// <summary>
    /// Adds the feature's segments.
    /// </summary>
    /// <param name="statusBar">The status bar to add them to.</param>
    void Register(StatusBarViewModel statusBar);
}
