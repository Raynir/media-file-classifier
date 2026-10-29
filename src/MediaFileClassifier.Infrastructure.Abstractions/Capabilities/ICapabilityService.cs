namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Which features this machine and platform support, each with a reason the UI can show (E01-S07-T03, D001).
/// </summary>
public interface ICapabilityService
{
    /// <summary>
    /// Raised after a flag's availability or reason changed.
    /// </summary>
    event EventHandler<CapabilityChangedEventArgs>? FlagChanged;

    /// <summary>
    /// Gets every flag registered so far.
    /// </summary>
    IReadOnlyList<CapabilityFlag> Flags { get; }

    /// <summary>
    /// Gets a capability's flag, registering it if needed. The same instance is returned every time,
    /// so a view model can expose it and the UI binds to its properties.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <returns>The flag.</returns>
    CapabilityFlag Get(CapabilityDefinition capability);

    /// <summary>
    /// Marks a capability available.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <param name="detail">What made it available, such as "FFmpeg 7.1 found." for video analysis.</param>
    void SetAvailable(CapabilityDefinition capability, string detail);

    /// <summary>
    /// Marks a capability unavailable. The reason reads "&lt;display name&gt; unavailable: &lt;cause&gt;".
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <param name="cause">Why, such as "FFmpeg not found." for video analysis.</param>
    void SetUnavailable(CapabilityDefinition capability, string cause);

    /// <summary>
    /// Runs every registered <see cref="ICapabilityContributor"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every contributor has finished.</returns>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
