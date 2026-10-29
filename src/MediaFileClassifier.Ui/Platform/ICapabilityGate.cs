using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

namespace MediaFileClassifier.Ui.Platform;

/// <summary>
/// Tells the command system whether a capability is available, and why not (E13-S05, E01-S07-T03).
/// Capability IDs are <see cref="CapabilityDefinition.Id"/> values, e.g. <c>platform.reveal-in-file-explorer</c>.
/// </summary>
public interface ICapabilityGate
{
    /// <summary>
    /// Raised on the UI thread when any capability changes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Checks a capability.
    /// </summary>
    /// <param name="capabilityId">Capability ID.</param>
    /// <returns>Whether it's available, and if not, a reason to show the user.</returns>
    CapabilityState Check(string capabilityId);
}

/// <summary>
/// Whether a capability is available.
/// </summary>
/// <param name="IsAvailable">Whether the capability can be used.</param>
/// <param name="Reason">Why it's unavailable, for a tooltip, or <c>null</c> when it's available.</param>
public readonly record struct CapabilityState(bool IsAvailable, string? Reason);

/// <summary>
/// The <see cref="ICapabilityGate"/> over the app's capability flags (<see cref="ICapabilityService"/>).
/// </summary>
public sealed class CapabilityServiceGate : ICapabilityGate
{
    private readonly ICapabilityService capabilities;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityServiceGate"/> class.
    /// </summary>
    /// <param name="capabilities">The capability flags.</param>
    public CapabilityServiceGate(ICapabilityService capabilities)
    {
        this.capabilities = capabilities;
        capabilities.FlagChanged += (_, _) => RaiseChanged();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    /// <remarks>A capability without a registered flag doesn't restrict anything, so it counts as available.</remarks>
    public CapabilityState Check(string capabilityId)
    {
        var flag = capabilities.Flags.FirstOrDefault(flag => flag.Id == capabilityId);
        return flag is null || flag.IsAvailable ? new(true, null) : new(false, flag.Reason);
    }

    // Flags can change on a background thread, such as a refresh after a dependency download,
    // and commands update bound UI.
    private void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
