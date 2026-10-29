using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Capabilities;

/// <summary>
/// Tests for <see cref="PlatformCapabilityContributor"/>, one per platform flag.
/// </summary>
public sealed class PlatformCapabilityContributorTests
{
    [Fact]
    public async Task RevealInFileExplorer_PlatformWithFileExplorer_IsAvailable()
    {
        var flag = await RefreshAsync(new FakePlatformServices { CanRevealInFileExplorer = true });

        Assert.True(flag.IsAvailable);
    }

    [Fact]
    public async Task RevealInFileExplorer_PlatformWithoutFileExplorer_IsUnavailableWithReason()
    {
        var flag = await RefreshAsync(new FakePlatformServices { CanRevealInFileExplorer = false });

        Assert.False(flag.IsAvailable);
        Assert.Equal("Show in file explorer unavailable: this platform has no file explorer.", flag.Reason);
    }

    private static async Task<CapabilityFlag> RefreshAsync(IPlatformServices platform)
    {
        await using var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(platform)
            .AddInfrastructure()
            .BuildServiceProvider();
        var capabilities = provider.GetRequiredService<ICapabilityService>();

        await capabilities.RefreshAsync(TestContext.Current.CancellationToken);

        return capabilities.Get(KnownCapabilities.RevealInFileExplorer);
    }
}
