namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// Settings stored inside a library, so they travel with it to another device. App-level settings live elsewhere.
/// </summary>
public sealed record LibrarySettings
{
    private readonly int retainedPreviousRuns = 1;

    /// <summary>
    /// Gets a value indicating whether new items are analyzed automatically after import (spec 1.4).
    /// </summary>
    public bool AnalyzeAutomatically { get; init; } = true;

    /// <summary>
    /// Gets how many previous analysis runs are kept per item and analyzer, besides the active one (D020).
    /// </summary>
    public int RetainedPreviousRuns
    {
        get => retainedPreviousRuns;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            retainedPreviousRuns = value;
        }
    }
}
