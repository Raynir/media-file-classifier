namespace MediaFileClassifier.UseCases.Activity;

/// <summary>
/// Tells the views that an event was added to the activity log (E15-S04), so an open activity log or the dashboard
/// can show it. It may be raised on any thread. A subscriber that throws is ignored.
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
    public void Raise()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        // A subscriber that throws must not fail the write that was recorded, nor stop the subscribers after it.
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // Its failure is its own.
            }
        }
    }
}
