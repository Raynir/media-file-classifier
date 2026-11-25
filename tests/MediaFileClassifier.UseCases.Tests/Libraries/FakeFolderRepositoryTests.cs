using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// The fake folder repository applies the real repository's availability and relink rules (MFC-1056), so UseCases tests
/// can catch a regression of them.
/// </summary>
public sealed class FakeFolderRepositoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_ReturnCopies_SoAChangeIsStoredOnlyByWritingIt()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos");
        await repository.AddAsync(folder, Token);

        var read = await repository.GetAsync(folder.Id, Token);
        read!.MarkOffline(Now);

        Assert.NotSame(read, repository.Stored(folder.Id));
        Assert.Equal(FolderStatus.Online, repository.Stored(folder.Id).Status);
        Assert.NotSame(repository.Stored(folder.Id), Assert.Single(await repository.GetAllAsync(Token)));
    }

    [Fact]
    public async Task UpdateAvailability_StoresOnlyTheAvailability_NotTheVolumeOrTheRules()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos", volumeId: "1A2B", label: "Backup");
        await repository.AddAsync(folder, Token);
        var stale = (await repository.GetAsync(folder.Id, Token))!;
        stale.MarkOffline(Now.AddHours(1));
        stale.ChangeVolumeId("9999");
        stale.ChangeVolumeLabel("Other");
        stale.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });

        await repository.UpdateAvailabilityAsync(stale, Token);

        var stored = repository.Stored(folder.Id);
        Assert.Equal((FolderStatus.Offline, Now.AddHours(1), "1A2B", "Backup"), (stored.Status, stored.StatusChangedAt, stored.VolumeId, stored.VolumeLabel));
        Assert.Empty(stored.Rules.ExcludePatterns);
    }

    [Fact]
    public async Task UpdateAvailabilityAndVolume_StoresTheLabel_AndKeepsTheStoredIdWhenTheCopyHasNone()
    {
        var repository = new FakeFolderRepository();
        await repository.AddAsync(Folder(@"E:\Photos", volumeId: "1A2B", label: "Backup"), Token);
        var copy = (await repository.GetAllAsync(Token))[0];
        copy.ChangeVolumeId(null);
        copy.ChangeVolumeLabel(null);

        await repository.UpdateAvailabilityAndVolumeAsync(copy, Token);

        var stored = repository.Stored(copy.Id);
        Assert.Equal(("1A2B", (string?)null), (stored.VolumeId, stored.VolumeLabel));
    }

    [Fact]
    public async Task UpdateAvailability_WhenTheRootChangedSinceTheCopyWasRead_StoresNothing()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos");
        await repository.AddAsync(folder, Token);
        var probed = (await repository.GetAsync(folder.Id, Token))!;
        var relinked = (await repository.GetAsync(folder.Id, Token))!;
        relinked.Relink(@"F:\Photos", @"F:\Photos", Now);
        Assert.True(await repository.RelinkAsync(relinked, @"E:\Photos", Token));
        probed.MarkOffline(Now.AddHours(1));

        await repository.UpdateAvailabilityAsync(probed, Token);
        await repository.UpdateAvailabilityAndVolumeAsync(probed, Token);

        var stored = repository.Stored(folder.Id);
        Assert.Equal((@"F:\Photos", FolderStatus.Online), (stored.PlatformHandle, stored.Status));
    }

    [Fact]
    public async Task Relink_StoresPathsAvailabilityAndVolume_ButNotTheRules()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos");
        await repository.AddAsync(folder, Token);
        var relinked = (await repository.GetAsync(folder.Id, Token))!;
        relinked.Relink(@"F:\Photos", @"F:\Photos", Now);
        relinked.ChangeVolumeId("1A2B");
        relinked.ChangeVolumeLabel("Backup");
        relinked.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });

        var stored = await repository.RelinkAsync(relinked, @"E:\Photos", Token);

        var now = repository.Stored(folder.Id);
        Assert.True(stored);
        Assert.Equal((@"F:\Photos", "1A2B", "Backup"), (now.PlatformHandle, now.VolumeId, now.VolumeLabel));
        Assert.Empty(now.Rules.ExcludePatterns);
    }

    [Fact]
    public async Task Relink_WhenTheFolderMovedSinceItWasRead_ReturnsFalse_AndStoresNothing()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos");
        await repository.AddAsync(folder, Token);
        var relinked = (await repository.GetAsync(folder.Id, Token))!;
        relinked.Relink(@"F:\Photos", @"F:\Photos", Now);

        var refusedStale = await repository.RelinkAsync(relinked, @"G:\Photos", Token);
        var refusedMissing = await repository.RelinkAsync(Folder(@"E:\Other"), @"E:\Other", Token);

        Assert.False(refusedStale);
        Assert.False(refusedMissing);
        Assert.Equal((@"E:\Photos", 0), (repository.Stored(folder.Id).PlatformHandle, repository.Relinks));
    }

    [Fact]
    public async Task Update_StoresTheWholeFolder_ButKeepsTheStoredVolumeWhenTheCopyHasNone()
    {
        var repository = new FakeFolderRepository();
        await repository.AddAsync(Folder(@"E:\Photos", volumeId: "1A2B", label: "Backup"), Token);
        var copy = (await repository.GetAllAsync(Token))[0];
        copy.ChangeVolumeId(null);
        copy.ChangeVolumeLabel(null);
        copy.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });

        await repository.UpdateAsync(copy, Token);

        var stored = repository.Stored(copy.Id);
        Assert.Equal(("1A2B", "Backup", 1), (stored.VolumeId, stored.VolumeLabel, stored.Rules.ExcludePatterns.Count));
    }

    [Fact]
    public async Task StatusChanged_IsRaisedWhenAWriteChangesTheStoredStatus_AndOnlyThen()
    {
        var repository = new FakeFolderRepository();
        var folder = Folder(@"E:\Photos");
        await repository.AddAsync(folder, Token);
        var raised = 0;
        repository.StatusChanged += (_, _) => raised++;
        var copy = (await repository.GetAsync(folder.Id, Token))!;

        await repository.UpdateAvailabilityAsync(copy, Token);
        copy.MarkOffline(Now);
        await repository.UpdateAvailabilityAsync(copy, Token);
        await repository.UpdateAvailabilityAsync(copy, Token);

        Assert.Equal(1, raised);
    }

    private static IndexedFolder Folder(string path, string? volumeId = null, string? label = null) =>
        new(
            Domain.Identity.Id<IndexedFolder>.New(),
            Domain.Identity.Id<Library>.New(),
            path,
            path,
            new FolderRules(),
            FolderStatus.Online,
            Now,
            Now,
            volumeLabel: label,
            volumeId: volumeId);
}
