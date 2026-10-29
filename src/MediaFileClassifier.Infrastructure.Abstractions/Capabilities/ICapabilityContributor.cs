namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Checks some capabilities and sets their flags, e.g. the platform, a native dependency, or the
/// registered models (E01-S05). Register one with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ICapabilityContributor, MyContributor&gt;())</c>;
/// <see cref="ICapabilityService.RefreshAsync"/> runs them all at startup.
/// </summary>
public interface ICapabilityContributor
{
    /// <summary>
    /// Checks the capabilities and sets their flags. An exception is logged and leaves the flags as they were.
    /// </summary>
    /// <param name="capabilities">Service to set the flags on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the flags are set.</returns>
    Task ContributeAsync(ICapabilityService capabilities, CancellationToken cancellationToken);
}
