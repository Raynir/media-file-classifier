namespace MediaFileClassifier.Domain.Activity;

/// <summary>
/// How an activity ended. The numbers are stored, so never reorder them.
/// </summary>
public enum ActivitySeverity
{
    /// <summary>It went as planned.</summary>
    Info = 0,

    /// <summary>It finished, but some items failed.</summary>
    Warning = 1,

    /// <summary>It failed.</summary>
    Error = 2,
}
