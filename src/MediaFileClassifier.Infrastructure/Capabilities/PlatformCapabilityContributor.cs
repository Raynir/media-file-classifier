using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Infrastructure.Capabilities;

/// <summary>
/// Sets the flags that depend only on the platform.
/// </summary>
/// <param name="platform">The platform services.</param>
public sealed class PlatformCapabilityContributor(IPlatformServices platform) : ICapabilityContributor
{
    /// <inheritdoc />
    public Task ContributeAsync(ICapabilityService capabilities, CancellationToken cancellationToken)
    {
        if (platform.CanRevealInFileExplorer)
        {
            capabilities.SetAvailable(KnownCapabilities.RevealInFileExplorer);
        }
        else
        {
            capabilities.SetUnavailable(KnownCapabilities.RevealInFileExplorer, CapabilityReasons.NoFileExplorerOnPlatform());
        }

        return Task.CompletedTask;
    }
}
