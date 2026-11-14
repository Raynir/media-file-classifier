using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Media;

namespace MediaFileClassifier.Infrastructure.Tests.Hashing;

/// <summary>
/// Tests for <see cref="MediaHasher"/>, and for storing and finding the hashes it computes (E01-S06-T02).
/// </summary>
public sealed class MediaHasherTests : IAsyncLifetime
{
    private readonly TemporaryFolder temp = new();
    private readonly MediaHasher hasher = new(new FileSystemMediaFileSource(), new XxHashFileHasher(), new SkiaPerceptualHasher());
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(temp.Combine("copies"));
        File.Copy(SampleMedia.JpegWithExif, temp.Combine("original.jpg"));
        File.Copy(SampleMedia.JpegWithExif, temp.Combine("copies", "renamed.jpg"));
        File.Copy(SampleMedia.WebP, temp.Combine("other.webp"));
        bed = await LibraryTestBed.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await bed.DisposeAsync();
        temp.Dispose();
    }

    [Fact]
    public async Task ComputeMissingAsync_IdenticalFiles_GiveIdenticalHashes()
    {
        var original = await HashAsync("original.jpg");
        var copy = await HashAsync("copies/renamed.jpg");
        var other = await HashAsync("other.webp");

        Assert.NotNull(original.ContentHash);
        Assert.NotNull(original.Fingerprint);
        Assert.NotNull(original.PerceptualHash);
        Assert.Equal(original, copy);
        Assert.NotEqual(original.ContentHash, other.ContentHash);
        Assert.NotEqual(original.Fingerprint, other.Fingerprint);
    }

    [Fact]
    public async Task ComputeMissingAsync_KeepsKnownHashes()
    {
        var known = new FileHashes { Fingerprint = 42, ContentHash = 43 };

        var hashes = await hasher.ComputeMissingAsync(Location("original.jpg"), Length("original.jpg"), MediaType.Video, known, Token);

        Assert.Equal(known, hashes);
    }

    [Fact]
    public async Task ComputeMissingAsync_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => hasher.ComputeMissingAsync(Location("missing.jpg"), 10, MediaType.Image, new FileHashes(), Token));
    }

    [Fact]
    public async Task StoredContentHash_FindsEveryIdenticalItem()
    {
        var items = new[] { bed.NewItem(1), bed.NewItem(2), bed.NewItem(3) };
        await bed.Library.Items.AddRangeAsync(items, Token);
        Assert.Equal(3, (await bed.Library.Items.GetWithoutContentHashAsync(10, Token)).Count);

        foreach (var (item, file) in items.Zip(["original.jpg", "copies/renamed.jpg", "other.webp"]))
        {
            item.SetHashes(await hasher.ComputeMissingAsync(Location(file), Length(file), item.MediaType, item.Hashes, Token));
            await bed.Library.Items.UpdateAsync(item, Token);
        }

        var identical = await bed.Library.Items.FindByContentHashAsync(items[0].Hashes.ContentHash!.Value, Token);

        Assert.Equivalent(new[] { items[0].Id, items[1].Id }, identical.Select(item => item.Id), strict: true);
        Assert.Empty(await bed.Library.Items.GetWithoutContentHashAsync(10, Token));
    }

    private async Task<FileHashes> HashAsync(string relativePath) =>
        await hasher.ComputeMissingAsync(Location(relativePath), Length(relativePath), MediaType.Image, new FileHashes(), Token);

    private FileLocation Location(string relativePath) => new(LocalFolder.ToHandle(temp.Path), relativePath);

    private long Length(string relativePath) => new FileInfo(temp.Combine(relativePath)).Length;
}
