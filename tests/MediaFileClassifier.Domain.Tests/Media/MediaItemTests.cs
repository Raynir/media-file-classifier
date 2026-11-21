using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Tests.Media;

/// <summary>
/// Tests for <see cref="MediaItem"/>, <see cref="RelativePath"/> and <see cref="MediaItemStatusTransitions"/>.
/// </summary>
public sealed class MediaItemTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(@"2024\Trip\IMG_0001.JPG", "2024/Trip/IMG_0001.JPG", "IMG_0001.JPG", "jpg")]
    [InlineData("clip.mp4", "clip.mp4", "clip.mp4", "mp4")]
    [InlineData("фото с пробелом.jpg", "фото с пробелом.jpg", "фото с пробелом.jpg", "jpg")]
    [InlineData(".hidden", ".hidden", ".hidden", "")]
    public void RelativePath_Normalizes(string input, string value, string fileName, string extension)
    {
        var path = RelativePath.From(input);

        Assert.Equal(value, path.Value);
        Assert.Equal(fileName, path.FileName);
        Assert.Equal(extension, path.Extension);
    }

    [Theory]
    [InlineData("/abs/file.jpg")]
    [InlineData(@"C:\Photos\file.jpg")]
    [InlineData("a/../file.jpg")]
    [InlineData("a//file.jpg")]
    [InlineData(" ")]
    public void RelativePath_RejectsInvalidPaths(string input)
    {
        Assert.Throws<ArgumentException>(() => RelativePath.From(input));
    }

    [Fact]
    public void Discover_StartsAsDiscoveredAndUnreviewed()
    {
        var item = CreateItem();

        Assert.Equal(MediaItemStatus.Discovered, item.Status);
        Assert.Equal(ReviewStatus.Unreviewed, item.ReviewStatus);
        Assert.Equal("jpg", item.FileFormat);
        Assert.Equal(Start, item.ImportedAt);
        Assert.Null(item.ReviewStatusChangedAt);
        Assert.Null(item.FavoriteChangedAt);
    }

    [Fact]
    public void SetFavorite_RecordsChangeTime()
    {
        var item = CreateItem();

        item.SetFavorite(true, Start.AddMinutes(1));

        Assert.True(item.IsFavorite);
        Assert.Equal(Start.AddMinutes(1), item.FavoriteChangedAt);
    }

    [Fact]
    public void SetFavorite_SameValue_KeepsChangeTime()
    {
        var item = CreateItem();

        item.SetFavorite(false, Start.AddMinutes(1));
        Assert.Null(item.FavoriteChangedAt);

        item.SetFavorite(true, Start.AddMinutes(2));
        item.SetFavorite(true, Start.AddMinutes(3));
        Assert.Equal(Start.AddMinutes(2), item.FavoriteChangedAt);
    }

    [Fact]
    public void MarkReviewedAndUnreviewed_RecordChangeTime()
    {
        var item = CreateItem();

        item.MarkReviewed(Start.AddMinutes(1));
        Assert.Equal(ReviewStatus.Reviewed, item.ReviewStatus);
        Assert.Equal(Start.AddMinutes(1), item.ReviewStatusChangedAt);

        item.MarkUnreviewed(Start.AddMinutes(2));
        Assert.Equal(ReviewStatus.Unreviewed, item.ReviewStatus);
        Assert.Equal(Start.AddMinutes(2), item.ReviewStatusChangedAt);
    }

    [Fact]
    public void MarkReviewedAndUnreviewed_SameValue_KeepChangeTime()
    {
        var item = CreateItem();

        item.MarkUnreviewed(Start.AddMinutes(1));
        Assert.Null(item.ReviewStatusChangedAt);

        item.MarkReviewed(Start.AddMinutes(2));
        item.MarkReviewed(Start.AddMinutes(3));
        Assert.Equal(Start.AddMinutes(2), item.ReviewStatusChangedAt);
    }

    [Fact]
    public void ChangeStatus_FollowsHappyPath()
    {
        var item = CreateItem();

        item.ChangeStatus(MediaItemStatus.Pending, Start.AddMinutes(1));
        item.ChangeStatus(MediaItemStatus.Indexed, Start.AddMinutes(2));

        Assert.Equal(MediaItemStatus.Indexed, item.Status);
        Assert.Equal(Start.AddMinutes(2), item.StatusChangedAt);
    }

    [Fact]
    public void ChangeStatus_RejectsDisallowedTransition()
    {
        var item = CreateItem();

        Assert.Throws<InvalidOperationException>(() => item.ChangeStatus(MediaItemStatus.Indexed, Start));
    }

    [Fact]
    public void ChangeStatus_KeepsReason()
    {
        var item = CreateItem();

        item.ChangeStatus(MediaItemStatus.Error, Start.AddMinutes(1), "Corrupt file");

        Assert.Equal("Corrupt file", item.StatusReason);
    }

    [Fact]
    public void MarkFailed_KeepsTheErrorUntilTheStatusChanges()
    {
        var item = CreateItem();
        var error = new MediaItemError("import.media", null, IsPermanent: true);

        item.MarkFailed(error, "The file is damaged.", Start.AddMinutes(1));

        Assert.Equal(MediaItemStatus.Error, item.Status);
        Assert.Equal("The file is damaged.", item.StatusReason);
        Assert.Equal(error, item.Error);

        item.ChangeStatus(MediaItemStatus.Pending, Start.AddMinutes(2));

        Assert.Null(item.Error);
        Assert.Null(item.StatusReason);
    }

    [Fact]
    public void MarkFailed_FromExcluded_IsRejected()
    {
        var item = CreateItem();
        item.ChangeStatus(MediaItemStatus.Excluded, Start);

        Assert.Throws<InvalidOperationException>(() => item.MarkFailed(new MediaItemError("import.media", null, false), "x", Start));
        Assert.Null(item.Error);
    }

    [Theory]
    [InlineData(MediaItemStatus.Missing, MediaItemStatus.Indexed, true)]
    [InlineData(MediaItemStatus.Error, MediaItemStatus.Pending, true)]
    [InlineData(MediaItemStatus.Indexed, MediaItemStatus.Discovered, false)]
    [InlineData(MediaItemStatus.Excluded, MediaItemStatus.Indexed, false)]
    public void Transitions_MatchDocumentedTable(MediaItemStatus from, MediaItemStatus to, bool allowed)
    {
        Assert.Equal(allowed, MediaItemStatusTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void Relocate_KeepsIdentity()
    {
        var item = CreateItem();
        var id = item.Id;
        var newFolder = Id<IndexedFolder>.New();

        item.Relocate(newFolder, RelativePath.From("renamed.jpg"));

        Assert.Equal(id, item.Id);
        Assert.Equal(newFolder, item.FolderId);
        Assert.Equal("renamed.jpg", item.FileName);
    }

    [Fact]
    public void ChangeExclusionReason_KeepsTheStatusAndWhenItChanged()
    {
        var item = CreateItem();
        item.ChangeStatus(MediaItemStatus.Excluded, Start, "Removed.");

        item.ChangeExclusionReason("Restore requested.");

        Assert.Equal(MediaItemStatus.Excluded, item.Status);
        Assert.Equal("Restore requested.", item.StatusReason);
        Assert.Equal(Start, item.StatusChangedAt);
    }

    [Fact]
    public void ChangeExclusionReason_OnAnItemThatIsNotExcluded_Throws()
    {
        var item = CreateItem();

        Assert.Throws<InvalidOperationException>(() => item.ChangeExclusionReason("Restore requested."));
    }

    private static MediaItem CreateItem() =>
        MediaItem.Discover(
            Id<IndexedFolder>.New(),
            RelativePath.From("2024/IMG_0001.JPG"),
            MediaType.Image,
            1024,
            Start.AddDays(-1),
            Start.AddDays(-1),
            fingerprint: 42,
            Start);
}
