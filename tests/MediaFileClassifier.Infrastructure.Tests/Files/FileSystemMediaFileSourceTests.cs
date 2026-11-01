using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;

namespace MediaFileClassifier.Infrastructure.Tests.Files;

/// <summary>
/// Tests for <see cref="FileSystemMediaFileSource"/>.
/// </summary>
public sealed class FileSystemMediaFileSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly FileSystemMediaFileSource source = new();
    private readonly FolderHandle folder;

    public FileSystemMediaFileSourceTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "2024", "trip"));
        File.WriteAllText(Path.Combine(root, "top.jpg"), "top");
        File.WriteAllText(Path.Combine(root, "2024", "trip", "фото с пробелом.jpg"), "nested");
        var hidden = Path.Combine(root, "Thumbs.db");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        folder = LocalFolder.ToHandle(root);
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
        File.SetLastWriteTimeUtc(Path.Combine(root, "top.jpg"), lastWrite);

        var stat = await source.GetStatAsync(new FileLocation(folder, "top.jpg"), TestContext.Current.CancellationToken);

        Assert.Equal(new MediaFileStat("top.jpg", 3, new DateTimeOffset(lastWrite)), stat);
    }

    [Fact]
    public async Task GetStatAsync_MissingFile_ReturnsNull()
    {
        Assert.Null(await source.GetStatAsync(new FileLocation(folder, "missing.jpg"), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
