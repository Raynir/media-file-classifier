using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Testing.Platform;
using MediaFileClassifier.UseCases.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="FolderLocations"/> (E04-S05): Auto runs as Poll on a network share and as Watch elsewhere, and
/// locations are asked once per Online root.
/// </summary>
public sealed class FolderLocationsTests
{
    private const string Share = @"\\nas\photos";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MonitoringMode.Auto, false, MonitoringMode.Watch)]
    [InlineData(MonitoringMode.Auto, true, MonitoringMode.Poll)]
    [InlineData(MonitoringMode.Watch, true, MonitoringMode.Watch)]
    [InlineData(MonitoringMode.Poll, false, MonitoringMode.Poll)]
    [InlineData(MonitoringMode.Manual, true, MonitoringMode.Manual)]
    public void EffectiveMode_ResolvesAutoFromTheLocation_AndKeepsAnyOtherMode(MonitoringMode mode, bool network, MonitoringMode expected)
    {
        var folder = Folder(Share, mode);

        Assert.Equal(expected, FolderLocations.EffectiveMode(folder, new FolderLocation(network)));
    }

    [Fact]
    public async Task GetAsync_OfAnOnlineFolder_AsksOnce()
    {
        var probe = new FakeFolderLocationProbe().Network(Share);
        var locations = new FolderLocations(NullLogger<FolderLocations>.Instance, probe);
        var folder = Folder(Share);

        Assert.True((await locations.GetAsync(folder, Token)).IsNetwork);
        Assert.True((await locations.GetAsync(folder, Token)).IsNetwork);
        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    public async Task GetAsync_OfAnOfflineFolder_AsksEachTime()
    {
        var probe = new FakeFolderLocationProbe();
        var locations = new FolderLocations(NullLogger<FolderLocations>.Instance, probe);
        var folder = Folder(@"Z:\Photos");
        folder.MarkOffline(DateTimeOffset.UnixEpoch);

        Assert.False((await locations.GetAsync(folder, Token)).IsNetwork);
        probe.Network(@"Z:\Photos");

        Assert.True((await locations.GetAsync(folder, Token)).IsNetwork);
        Assert.Equal(2, probe.Asked);
    }

    [Fact]
    public async Task GetAsync_WhenTheProbeFails_TakesTheFolderAsLocalForNow_AndAsksAgain()
    {
        var probe = new FakeFolderLocationProbe { Error = new DriveNotFoundException("Drive Z: isn't there now.") };
        var locations = new FolderLocations(NullLogger<FolderLocations>.Instance, probe);
        var folder = Folder(@"Z:\Photos");

        Assert.Equal(FolderLocation.Local, await locations.GetAsync(folder, Token));
        Assert.Equal(MonitoringMode.Watch, await locations.GetEffectiveModeAsync(folder, Token));

        // The mapped drive is reconnected.
        probe.Error = null;
        probe.Network(@"Z:\Photos");
        Assert.Equal(MonitoringMode.Poll, await locations.GetEffectiveModeAsync(folder, Token));
    }

    [Fact]
    public async Task GetEffectiveModeAsync_OfAFolderNotInAuto_DoesNotAsk()
    {
        var probe = new FakeFolderLocationProbe().Network(Share);
        var locations = new FolderLocations(NullLogger<FolderLocations>.Instance, probe);

        Assert.Equal(MonitoringMode.Watch, await locations.GetEffectiveModeAsync(Folder(Share, MonitoringMode.Watch), Token));
        Assert.Equal(0, probe.Asked);
    }

    private static IndexedFolder Folder(string path, MonitoringMode mode = MonitoringMode.Auto)
    {
        var folder = IndexedFolder.Create(Id<Library>.New(), path, path, DateTimeOffset.UnixEpoch);
        folder.ChangeMonitoringMode(mode);
        return folder;
    }
}
