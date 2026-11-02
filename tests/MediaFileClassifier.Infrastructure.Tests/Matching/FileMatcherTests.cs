using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Matching;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Matching;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Media;
using SkiaSharp;

namespace MediaFileClassifier.Infrastructure.Tests.Matching;

/// <summary>
/// Tests for <see cref="FileMatcher"/>: every match tier, with files from the sample corpus.
/// </summary>
public sealed class FileMatcherTests : IAsyncLifetime
{
    private readonly MediaHasher hasher = new(new FileSystemMediaFileSource(), new XxHashFileHasher(), new SkiaPerceptualHasher());
    private readonly Dictionary<string, MediaItem> library = [];
    private LibraryTestBed bed = null!;
    private IFileMatcher matcher = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        foreach (var (path, mediaType) in new[]
        {
            (SampleMedia.JpegWithExif, MediaType.Image),
            (SampleMedia.WebP, MediaType.Image),
            (SampleMedia.AnimatedGif, MediaType.AnimatedImage),
            (SampleMedia.ShortVideo, MediaType.Video),
        })
        {
            var facts = await FactsAsync(path, mediaType);
            var item = MediaItem.Discover(
                bed.Folder.Id, RelativePath.From($"library/{facts.FileName}"), mediaType, facts.SizeBytes, facts.FileModifiedAt, facts.FileModifiedAt, null, LibraryTestBed.Start);
            item.SetHashes(facts.Hashes);
            library[path] = item;
        }

        await bed.Library.Items.AddRangeAsync(library.Values, Token);
        matcher = await new FileMatcherFactory().CreateAsync(bed.Library.Items, Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task MatchAsync_RenamedIdenticalCopy_MatchesByContentHash()
    {
        var facts = await FactsAsync(SampleMedia.ShortVideo, MediaType.Video) with { FileName = "renamed.mp4" };

        var match = await matcher.MatchAsync(facts, Token);

        AssertMatch(match, FileMatchType.ContentHash, MatchConfidence.Certain, SampleMedia.ShortVideo);
    }

    [Fact]
    public async Task MatchAsync_OnlyFingerprintKnown_MatchesByFingerprintAndSize()
    {
        // During discovery only the fingerprint is computed.
        var facts = await FactsAsync(SampleMedia.JpegWithExif, MediaType.Image);
        facts = facts with { FileName = "moved.jpg", Hashes = new FileHashes { Fingerprint = facts.Hashes.Fingerprint } };

        var match = await matcher.MatchAsync(facts, Token);

        AssertMatch(match, FileMatchType.Fingerprint, MatchConfidence.High, SampleMedia.JpegWithExif);
    }

    [Fact]
    public async Task MatchAsync_SameFingerprintButDifferentContentHash_IsNoFingerprintMatch()
    {
        var facts = await FactsAsync(SampleMedia.ShortVideo, MediaType.Video);
        facts = facts with { FileName = "other.mp4", Hashes = facts.Hashes with { ContentHash = 12345 } };

        var match = await matcher.MatchAsync(facts, Token);

        Assert.Equal(FileMatchType.None, match.Type);
    }

    [Fact]
    public async Task MatchAsync_ResizedAndRecompressedImage_MatchesPerceptually()
    {
        var copy = Path.Combine(Path.GetTempPath(), $"mfc-match-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var bitmap = SKBitmap.Decode(SampleMedia.WebP))
            using (var resized = bitmap.Resize(new SKImageInfo(bitmap.Width / 2, bitmap.Height / 2), new SKSamplingOptions(SKCubicResampler.Mitchell)))
            using (var data = SKImage.FromBitmap(resized).Encode(SKEncodedImageFormat.Jpeg, 40))
            {
                await File.WriteAllBytesAsync(copy, data.ToArray(), Token);
            }

            var match = await matcher.MatchAsync(await FactsAsync(copy, MediaType.Image), Token);

            AssertMatch(match, FileMatchType.Perceptual, MatchConfidence.Medium, SampleMedia.WebP);
            Assert.InRange(match.Items[0].Distance!.Value, 0, PerceptualHash.SamePictureThreshold);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task MatchAsync_PerceptualHashOfAVideo_IsIgnored()
    {
        var facts = await FactsAsync(SampleMedia.JpegWithExif, MediaType.Image);
        facts = new FileFacts("clip.mp4", MediaType.Video, 1, facts.FileModifiedAt.AddDays(1), new FileHashes { PerceptualHash = facts.Hashes.PerceptualHash });

        Assert.Equal(FileMatch.None, await matcher.MatchAsync(facts, Token));
    }

    [Fact]
    public async Task MatchAsync_EditedFileWithSameNameAndSize_MatchesByFileNameAndMetadata()
    {
        // E.g. a video whose embedded metadata was edited in place: every hash changed, the name and size didn't.
        var original = library[SampleMedia.ShortVideo];
        var facts = new FileFacts(
            original.FileName.ToUpperInvariant(), MediaType.Video, original.SizeBytes, original.FileModifiedAt.AddHours(1), new FileHashes { Fingerprint = 1, ContentHash = 2 });

        var match = await matcher.MatchAsync(facts, Token);

        AssertMatch(match, FileMatchType.FileNameAndMetadata, MatchConfidence.Low, SampleMedia.ShortVideo);
    }

    [Fact]
    public async Task MatchAsync_SameNameAndModificationTime_MatchesByFileNameAndMetadata()
    {
        var original = library[SampleMedia.ShortVideo];
        var facts = new FileFacts(original.FileName, MediaType.Video, original.SizeBytes + 10, original.FileModifiedAt.AddMilliseconds(1), new FileHashes());

        var match = await matcher.MatchAsync(facts, Token);

        AssertMatch(match, FileMatchType.FileNameAndMetadata, MatchConfidence.Low, SampleMedia.ShortVideo);
    }

    [Fact]
    public async Task MatchAsync_SameNameButOtherSizeTimeOrType_DoesNotMatch()
    {
        var original = library[SampleMedia.ShortVideo];
        var otherSizeAndTime = new FileFacts(original.FileName, MediaType.Video, original.SizeBytes + 10, original.FileModifiedAt.AddHours(1), new FileHashes());
        var otherType = new FileFacts(original.FileName, MediaType.Image, original.SizeBytes, original.FileModifiedAt, new FileHashes());

        Assert.Equal(FileMatch.None, await matcher.MatchAsync(otherSizeAndTime, Token));
        Assert.Equal(FileMatch.None, await matcher.MatchAsync(otherType, Token));
    }

    [Fact]
    public async Task MatchAsync_UnrelatedFile_DoesNotMatch()
    {
        var match = await matcher.MatchAsync(await FactsAsync(SampleMedia.ImageWithText, MediaType.Image), Token);

        Assert.Equal(FileMatch.None, match);
    }

    [Fact]
    public async Task MatchAsync_DuplicatesInLibrary_MatchesEveryCopy()
    {
        var original = library[SampleMedia.JpegWithExif];
        var duplicate = MediaItem.Discover(
            bed.Folder.Id, RelativePath.From("other/copy.jpg"), MediaType.Image, original.SizeBytes, original.FileModifiedAt, original.FileModifiedAt, null, LibraryTestBed.Start);
        duplicate.SetHashes(original.Hashes);
        await bed.Library.Items.AddAsync(duplicate, Token);

        var match = await matcher.MatchAsync(await FactsAsync(SampleMedia.JpegWithExif, MediaType.Image), Token);

        Assert.Equal(FileMatchType.ContentHash, match.Type);
        Assert.Equivalent(new[] { original.Id, duplicate.Id }, match.Items.Select(item => item.ItemId));
    }

    private async Task<FileFacts> FactsAsync(string path, MediaType mediaType)
    {
        var info = new FileInfo(path);
        var hashes = await hasher.ComputeMissingAsync(LocalFolder.ToLocation(path), info.Length, mediaType, new FileHashes(), Token);
        return new FileFacts(info.Name, mediaType, info.Length, new DateTimeOffset(info.LastWriteTimeUtc), hashes);
    }

    private void AssertMatch(FileMatch match, FileMatchType type, MatchConfidence confidence, string expectedPath)
    {
        Assert.Equal(type, match.Type);
        Assert.Equal(confidence, match.Confidence);
        Assert.Equal(library[expectedPath].Id, Assert.Single(match.Items).ItemId);
    }
}
