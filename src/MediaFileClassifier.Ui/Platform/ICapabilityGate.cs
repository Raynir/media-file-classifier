namespace MediaFileClassifier.Ui.Platform;

/// <summary>
/// Tells the command system whether a capability is available, and why not (E13-S05, E01-S07-T03).
/// </summary>
/// <remarks>
/// A narrow seam over the capability flags service from MFC-57 (<c>ICapabilityService</c>), which isn't on
/// <c>develop</c> yet. Once it is, an adapter over it replaces <see cref="AllCapabilitiesAvailable"/>.
/// Capability IDs are the same strings as <c>CapabilityDefinition.Id</c>.
/// </remarks>
public interface ICapabilityGate
{
    /// <summary>
    /// Raised when any capability changes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Checks a capability.
    /// </summary>
    /// <param name="capabilityId">Capability ID, e.g. <c>platform.reveal-in-file-explorer</c>.</param>
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
/// The stand-in <see cref="ICapabilityGate"/> until the capability flags service (MFC-57) is merged:
/// every capability is available.
/// </summary>
public sealed class AllCapabilitiesAvailable : ICapabilityGate
{
    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public CapabilityState Check(string capabilityId) => new(true, null);
}
