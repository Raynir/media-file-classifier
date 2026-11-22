namespace MediaFileClassifier.UseCases.Activity;

/// <summary>
/// Tells the views that an event was added to the activity log (E15-S04), so an open activity log or the dashboard
/// can show it. It may be raised on any thread.
/// </summary>
public sealed class ActivityChanges
{
    /// <summary>
    /// Raised after an event is stored.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raises <see cref="Changed"/>.
    /// </summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
