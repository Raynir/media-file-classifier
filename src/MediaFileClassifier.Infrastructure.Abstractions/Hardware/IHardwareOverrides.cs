namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// The user's choices on the hardware settings (E17-S08-T02): the execution provider and the model tier the AI
/// providers use instead of choosing them from the hardware. Providers read them on every call, so a change applies
/// to the next analysis.
/// </summary>
public interface IHardwareOverrides
{
    /// <summary>
    /// Raised after a choice changed, on the thread that changed it.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets the execution provider to try first, or <see langword="null"/> to choose it from the hardware.
    /// </summary>
    ExecutionProvider? ExecutionProvider { get; }

    /// <summary>
    /// Gets the model tier to use, e.g. <c>compact</c>, or <see langword="null"/> to use the installed tier that suits
    /// the execution provider. A tier that isn't installed falls back to that choice.
    /// </summary>
    string? TierId { get; }
}
