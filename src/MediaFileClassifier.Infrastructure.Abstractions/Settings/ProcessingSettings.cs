namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Background processing settings (E05-S01). The settings UI comes with E17; until then the defaults apply.
/// </summary>
public sealed record ProcessingSettings
{
    /// <summary>
    /// Gets the I/O pool's concurrency limit, or <c>null</c> for the default of 4.
    /// </summary>
    public int? IoConcurrency { get; init; }

    /// <summary>
    /// Gets the CPU pool's concurrency limit, or <c>null</c> for the hardware default (E01-S07-T04).
    /// </summary>
    public int? CpuConcurrency { get; init; }

    /// <summary>
    /// Gets the AI pool's concurrency limit, or <c>null</c> for the default of 1.
    /// </summary>
    public int? AiConcurrency { get; init; }

    /// <summary>
    /// Gets what happens to unfinished work when a library opens.
    /// </summary>
    public ResumeMode ResumeOnOpen { get; init; } = ResumeMode.Automatic;

    /// <summary>
    /// Gets whether closing the app while work is pending asks first (D027). "Don't ask again" turns it off.
    /// </summary>
    public bool ConfirmQuitWhileProcessing { get; init; } = true;
}
