using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Files;

/// <summary>
/// Tests for <see cref="LocalFolder"/>.
/// </summary>
public sealed class LocalFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mfc-tests", "root");

    [Fact]
    public void ToHandle_UsesFullPathAsIdAndFolderNameAsDisplayName()
    {
        var handle = LocalFolder.ToHandle(Root + Path.DirectorySeparatorChar);

        Assert.Equal(new FolderHandle(Root, "root"), handle);
    }

    [Fact]
    public void ToLocation_SplitsParentFolderAndFileName()
    {
        var location = LocalFolder.ToLocation(Path.Combine(Root, "photo.jpg"));

        Assert.Equal(new FileLocation(LocalFolder.ToHandle(Root), "photo.jpg"), location);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData("2024/trip/photo.jpg", "2024|trip|photo.jpg")]
    [InlineData("2024\\trip\\photo.jpg", "2024|trip|photo.jpg")]
    [InlineData("2024/../photo.jpg", "photo.jpg")]
    public void GetFullPath_PathInsideFolder_CombinesWithRoot(string relativePath, string expected)
    {
        var fullPath = LocalFolder.GetFullPath(LocalFolder.ToHandle(Root), relativePath);

        Assert.Equal(Path.Combine(Root, expected.Replace('|', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar), fullPath);
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("a/../../outside.jpg")]
    [InlineData("../root-sibling/photo.jpg")]
    public void GetFullPath_PathLeavingFolder_Throws(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => LocalFolder.GetFullPath(LocalFolder.ToHandle(Root), relativePath));
    }

    [Fact]
    public void GetFullPath_RootedPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalFolder.GetFullPath(LocalFolder.ToHandle(Root), Path.GetTempPath()));
    }

    [Fact]
    public void GetRelativePath_UsesForwardSlashes()
    {
        var relativePath = LocalFolder.GetRelativePath(LocalFolder.ToHandle(Root), Path.Combine(Root, "2024", "photo.jpg"));

        Assert.Equal("2024/photo.jpg", relativePath);
    }
}
