namespace MediaFileClassifier.Domain.Activity;

/// <summary>
/// How much of the activity log a library keeps (E15-S04).
/// </summary>
public static class ActivityRetention
{
    /// <summary>
    /// The number of newest events kept. Recording an event drops the oldest ones beyond it.
    /// </summary>
    public const int MaxEntries = 5000;
}
