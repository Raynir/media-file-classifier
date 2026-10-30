using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

namespace MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;

/// <summary>
/// Checks at startup that a native dependency loads, and which version it is (E01-S08-T03). Register one with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;INativeDependencyProbe, MyProbe&gt;())</c>, and list it
/// in <c>docs/native-dependencies.md</c>.
/// </summary>
public interface INativeDependencyProbe
{
    /// <summary>
    /// Gets the dependency's name for the log, e.g. "FFmpeg".
    /// </summary>
    string DependencyName { get; }

    /// <summary>
    /// Gets the capabilities that need this dependency. They are disabled when it is unavailable.
    /// </summary>
    IReadOnlyList<CapabilityDefinition> Capabilities { get; }

    /// <summary>
    /// Checks the dependency. Report problems in the status rather than by throwing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was found.</returns>
    Task<NativeDependencyStatus> ProbeAsync(CancellationToken cancellationToken);
}
