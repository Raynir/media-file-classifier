using LibVLCSharp.Shared;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;

namespace MediaFileClassifier.Infrastructure.Playback;

/// <summary>
/// Loads LibVLC at startup and reports its version (D070). It sets the <see cref="KnownCapabilities.VideoPlayback"/>
/// flag; without LibVLC, video opens in the OS default app (D026).
/// </summary>
/// <param name="runtime">The LibVLC instance.</param>
public sealed class LibVlcProbe(LibVlcRuntime runtime) : INativeDependencyProbe
{
    /// <inheritdoc />
    public string DependencyName => "LibVLC";

    /// <inheritdoc />
    public IReadOnlyList<CapabilityDefinition> Capabilities { get; } = [KnownCapabilities.VideoPlayback];

    /// <inheritdoc />
    public Task<NativeDependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var libVlc = runtime.GetInstance();
            return Task.FromResult(NativeDependencyStatus.Available(libVlc.Version, AppContext.BaseDirectory));
        }
        catch (Exception ex) when (ex is VLCException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return Task.FromResult(NativeDependencyStatus.Unavailable(CapabilityReasons.FailedToLoad(DependencyName), detail: ex.Message));
        }
    }
}
