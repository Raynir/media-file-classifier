using System.ComponentModel;

namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Whether a capability is available, and why. The UI binds to <see cref="IsAvailable"/> and <see cref="Reason"/>;
/// changes raise <see cref="PropertyChanged"/> on the thread that made them.
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
        Reason = $"{definition.DisplayName} unavailable: not checked yet.";
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
    /// Gets a message for the user, such as "Video analysis unavailable: FFmpeg not found." when unavailable.
    /// </summary>
    public string Reason { get; private set; }

    /// <summary>
    /// Changes the state. Call it through <see cref="ICapabilityService"/>, which also raises its change event.
    /// </summary>
    /// <param name="isAvailable">Whether the feature can be used.</param>
    /// <param name="reason">Message for the user.</param>
    /// <returns><see langword="true"/> if anything changed.</returns>
    public bool Update(bool isAvailable, string reason)
    {
        var availabilityChanged = IsAvailable != isAvailable;
        var reasonChanged = !string.Equals(Reason, reason, StringComparison.Ordinal);
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

    /// <inheritdoc />
    public override string ToString() => Reason;
}
