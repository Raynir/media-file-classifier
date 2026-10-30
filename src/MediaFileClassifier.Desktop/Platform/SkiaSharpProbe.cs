using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using SkiaSharp;

namespace MediaFileClassifier.Desktop.Platform;

/// <summary>
/// Reports the native SkiaSharp library that Avalonia renders with. The UI can't start without it,
/// so it gates no capability; the probe only puts its version in the log.
/// </summary>
internal sealed class SkiaSharpProbe : INativeDependencyProbe
{
    /// <inheritdoc />
    public string DependencyName => "SkiaSharp";

    /// <inheritdoc />
    public IReadOnlyList<CapabilityDefinition> Capabilities => [];

    /// <inheritdoc />
    public Task<NativeDependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        var version = SkiaSharpVersion.Native.ToString();
        return Task.FromResult(NativeDependencyStatus.Available(version, location: null));
    }
}
