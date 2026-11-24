using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Files;

/// <summary>
/// Tests for <see cref="PathFolderLocationProbe"/> (E04-S05-T02).
/// </summary>
public sealed class PathFolderLocationProbeTests
{
    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\Photos", true)]
    [InlineData("//server/share/Photos", true)]
    [InlineData(@"\\?\UNC\server\share\Photos", true)]
    [InlineData(@"\\.\UNC\server\share", true)]
    [InlineData(@"\\?\C:\Photos", false)]
    [InlineData(@"\\.\C:\Photos", false)]
    [InlineData(@"C:\Photos", false)]
    [InlineData(@"\\", false)]
    [InlineData(@"\\\server", false)]
    [InlineData("", false)]
    public void IsUncPath_TellsUncPathsFromLocalOnes(string path, bool expected)
    {
        Assert.Equal(expected, PathFolderLocationProbe.IsUncPath(path));
    }

    [Fact]
    public async Task ProbeAsync_UncFolder_IsNetworkAndNotCloud()
    {
        var location = await new PathFolderLocationProbe().ProbeAsync(new FolderHandle(@"\\nas\media\Photos", "Photos"), TestContext.Current.CancellationToken);

        Assert.Equal(new FolderLocation(IsNetwork: true), location);
        Assert.False(location.IsCloud);
    }

    [Fact]
    public async Task ProbeAsync_LocalFolder_IsLocal()
    {
        var location = await new PathFolderLocationProbe().ProbeAsync(new FolderHandle(@"C:\Photos", "Photos"), TestContext.Current.CancellationToken);

        Assert.Same(FolderLocation.Local, location);
    }
}
