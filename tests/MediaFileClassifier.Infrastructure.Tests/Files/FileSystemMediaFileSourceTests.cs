using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Files;

/// <summary>
/// Tests for <see cref="FileSystemMediaFileSource"/>.
/// </summary>
public sealed class FileSystemMediaFileSourceTests : IDisposable
{
    private readonly TemporaryFolder temp = new();
    private readonly FileSystemMediaFileSource source = new();
    private readonly FolderHandle folder;

    public FileSystemMediaFileSourceTests()
    {
        Directory.CreateDirectory(temp.Combine("2024", "trip"));
        File.WriteAllText(temp.Combine("top.jpg"), "top");
        File.WriteAllText(temp.Combine("2024", "trip", "фото с пробелом.jpg"), "nested");
        var hidden = temp.Combine("Thumbs.db");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        folder = LocalFolder.ToHandle(temp.Path);
    }

    [Fact]
    public async Task EnumerateFilesAsync_ListsFilesRecursivelyWithRelativePaths()
    {
        var files = await source.EnumerateFilesAsync(folder, cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["2024/trip/фото с пробелом.jpg", "top.jpg"], files.Select(file => file.RelativePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnumerateFilesAsync_Subfolder_ListsOnlyItsFiles()
    {
        var files = await source.EnumerateFilesAsync(folder, "2024", TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        var file = Assert.Single(files);
        Assert.Equal("2024/trip/фото с пробелом.jpg", file.RelativePath);
        Assert.Equal(6, file.Length);
    }

    [Fact]
    public async Task EnumerateFilesAsync_MissingFolder_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            async () => await source.EnumerateFilesAsync(folder, "missing", TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenReadAsync_ReadsContent()
    {
        await using var stream = await source.OpenReadAsync(new FileLocation(folder, "2024/trip/фото с пробелом.jpg"), TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        Assert.Equal("nested", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenReadAsync_PathLeavingFolder_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => source.OpenReadAsync(new FileLocation(folder, "../outside.jpg"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatAsync_ExistingFile_ReturnsSizeAndTime()
    {
        var lastWrite = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(temp.Combine("top.jpg"), lastWrite);

        var stat = await source.GetStatAsync(new FileLocation(folder, "top.jpg"), TestContext.Current.CancellationToken);

        Assert.Equal(new MediaFileStat("top.jpg", 3, new DateTimeOffset(lastWrite)), stat! with { CreatedUtc = null });
        Assert.NotNull(stat.CreatedUtc);
    }

    [Fact]
    public async Task GetStatAsync_MissingFile_ReturnsNull()
    {
        Assert.Null(await source.GetStatAsync(new FileLocation(folder, "missing.jpg"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"C:\Photos", @"C:\Photos", "")]
    [InlineData(@"C:\Photos\2024\trip", @"C:\Photos", "2024/trip")]
    [InlineData(@"C:\photos\2024\", @"C:\Photos\", "2024")]
    [InlineData(@"C:\Photos2", @"C:\Photos", null)]
    [InlineData(@"C:\Photos", @"C:\Photos\2024", null)]
    [InlineData(@"D:\Photos", @"C:\Photos", null)]
    [InlineData("content://tree/primary%3ADCIM", @"C:\Photos", null)]
    public void GetPathInside_ComparesFullPaths(string path, string parent, string? expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The paths are Windows paths.");

        Assert.Equal(expected, source.GetPathInside(new FolderHandle(path, path), new FolderHandle(parent, parent)));
    }

    [Fact]
    public async Task IsAvailableAsync_IsTrueOnlyForAFolderThatCanBeListed()
    {
        Assert.True(await source.IsAvailableAsync(folder, TestContext.Current.CancellationToken));
        Assert.False(await source.IsAvailableAsync(LocalFolder.ToHandle(temp.Combine("unplugged")), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        temp.Dispose();
    }
}
