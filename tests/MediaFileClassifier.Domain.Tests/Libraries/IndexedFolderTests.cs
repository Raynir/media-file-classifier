using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Domain.Tests.Libraries;

/// <summary>
/// Tests for <see cref="IndexedFolder"/>.
/// </summary>
public sealed class IndexedFolderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_IsOnlineWithDefaultRules()
    {
        var folder = IndexedFolder.Create(Id<Library>.New(), "/sdcard/DCIM", "content://media/tree/primary%3ADCIM", Start);

        Assert.Equal(FolderStatus.Online, folder.Status);
        Assert.True(folder.Rules.IncludeSubfolders);
        Assert.Equal("content://media/tree/primary%3ADCIM", folder.PlatformHandle);
    }

    [Fact]
    public void MarkOffline_RecordsWhenStatusChanged()
    {
        var folder = IndexedFolder.Create(Id<Library>.New(), @"E:\Photos", @"E:\Photos", Start);

        folder.MarkOffline(Start.AddHours(1));
        folder.MarkOffline(Start.AddHours(2));

        Assert.Equal(FolderStatus.Offline, folder.Status);
        Assert.Equal(Start.AddHours(1), folder.StatusChangedAt);
    }

    [Fact]
    public void Relink_ChangesRootAndBringsFolderOnline()
    {
        var folder = IndexedFolder.Create(Id<Library>.New(), @"E:\Photos", @"E:\Photos", Start);
        folder.MarkOffline(Start.AddHours(1));

        folder.Relink(@"F:\Photos", @"F:\Photos", Start.AddHours(2));

        Assert.Equal(@"F:\Photos", folder.PlatformHandle);
        Assert.Equal(FolderStatus.Online, folder.Status);
    }

    [Theory]
    [InlineData("  Backup-2TB ", "Backup-2TB")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Constructor_TrimsTheVolumeLabel_LikeChangeVolumeLabel(string? stored, string? expected)
    {
        var folder = new IndexedFolder(
            Id<IndexedFolder>.New(), Id<Library>.New(), @"E:\Photos", @"E:\Photos", new FolderRules(), FolderStatus.Offline, Start, Start, volumeLabel: stored);

        Assert.Equal(expected, folder.VolumeLabel);
        Assert.False(folder.ChangeVolumeLabel(stored));
    }

    [Fact]
    public void Library_RenameRejectsBlankName()
    {
        var library = Library.Create("Photos", Start);

        Assert.Throws<ArgumentException>(() => library.Rename("  ", Start.AddHours(1)));
    }
}
