namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Data of <see cref="ICapabilityService.FlagChanged"/>.
/// </summary>
/// <param name="flag">The flag that changed.</param>
public sealed class CapabilityChangedEventArgs(CapabilityFlag flag) : EventArgs
{
    /// <summary>
    /// Gets the flag that changed.
    /// </summary>
    public CapabilityFlag Flag { get; } = flag;
}
