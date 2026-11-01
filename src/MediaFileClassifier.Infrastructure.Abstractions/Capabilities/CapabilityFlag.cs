using System.ComponentModel;

namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Whether a capability is available, and why not. The UI binds to <see cref="IsAvailable"/> and turns
/// <see cref="Reason"/> into text; changes raise <see cref="PropertyChanged"/> on the thread that made them.
/// </summary>
public sealed class CapabilityFlag : INotifyPropertyChanged
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityFlag"/> class, unavailable until it is checked.
    /// </summary>
    /// <param name="definition">The capability.</param>
    public CapabilityFlag(CapabilityDefinition definition)
    {
        Definition = definition;
        Reason = CapabilityReasons.NotCheckedYet();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the capability.
    /// </summary>
    public CapabilityDefinition Definition { get; }

    /// <summary>
    /// Gets the capability's identifier.
    /// </summary>
    public string Id => Definition.Id;

    /// <summary>
    /// Gets a value indicating whether the feature can be used.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Gets why the feature is unavailable, or <see langword="null"/> when it's available.
    /// </summary>
    public CapabilityReason? Reason { get; private set; }

    /// <summary>
    /// Marks the capability available. Call it through <see cref="ICapabilityService"/>, which also raises its change event.
    /// </summary>
    /// <returns><see langword="true"/> if anything changed.</returns>
    public bool MarkAvailable() => Update(isAvailable: true, reason: null);

    /// <summary>
    /// Marks the capability unavailable. Call it through <see cref="ICapabilityService"/>, which also raises its change event.
    /// </summary>
    /// <param name="reason">Why.</param>
    /// <returns><see langword="true"/> if anything changed.</returns>
    public bool MarkUnavailable(CapabilityReason reason) => Update(isAvailable: false, reason);

    /// <inheritdoc />
    public override string ToString() => IsAvailable ? $"{Id}: available" : $"{Id}: unavailable, {Reason}";

    private bool Update(bool isAvailable, CapabilityReason? reason)
    {
        var availabilityChanged = IsAvailable != isAvailable;
        var reasonChanged = !Equals(Reason, reason);
        IsAvailable = isAvailable;
        Reason = reason;
        if (availabilityChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
        }

        if (reasonChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
        }

        return availabilityChanged || reasonChanged;
    }
}
