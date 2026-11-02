using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryLayout"/> (E01-S03-T01).
/// </summary>
public sealed class LibraryLayoutTests
{
    [Fact]
    public void For_PutsFilesInsideMfcFolder()
    {
        var layout = LibraryLayout.For(@"D:\Media", "Family");

        Assert.Equal(@"D:\Media\Family.mfc", layout.RootDirectory);
        Assert.Equal(@"D:\Media\Family.mfc\library.db", layout.DatabasePath);
        Assert.Equal(@"D:\Media\Family.mfc\library.lock", layout.LockFilePath);
        Assert.Equal(@"D:\Media\Family.mfc\thumbnails", layout.ThumbnailsDirectory);
        Assert.Equal(@"D:\Media\Family.mfc\backups", layout.BackupsDirectory);
    }

    [Fact]
    public void For_NameWithInvalidCharacter_Throws()
    {
        Assert.Throws<ArgumentException>(() => LibraryLayout.For(@"D:\Media", "a/b"));
    }

    [Fact]
    public void GetThumbnailPath_ShardsByLastTwoHexDigitsOfId()
    {
        var layout = LibraryLayout.For(@"D:\Media", "Family");
        var id = new Id<MediaItem>(Guid.Parse("019a3b2c-0000-7000-8000-0000000000a7"));

        var path = layout.GetThumbnailPath(id, ThumbnailSize.Small);

        Assert.Equal(@"D:\Media\Family.mfc\thumbnails\a7\019a3b2c-0000-7000-8000-0000000000a7-256.webp", path);
    }

    [Fact]
    public void GetThumbnailPath_SpreadsNewIdsAcrossShards()
    {
        var layout = LibraryLayout.For(@"D:\Media", "Family");

        var shards = Enumerable.Range(0, 2000)
            .Select(_ => Path.GetFileName(Path.GetDirectoryName(layout.GetThumbnailPath(Id<MediaItem>.New(), ThumbnailSize.Large))))
            .Distinct()
            .Count();

        // UUIDv7s made in the same millisecond share their first digits; the last ones are random.
        Assert.True(shards > 200, $"Only {shards} of 256 shards used.");
    }
}
