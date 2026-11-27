using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Duplicates;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// End-to-end tests of <see cref="DuplicateGroupMaintainer"/> with the real import pipeline, library and job system
/// (E12-S01-T02): importing a copy of a file creates or extends its exact group.
/// </summary>
public sealed class DuplicateGroupImportTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;
    private DuplicateGroupMaintainer maintainer = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        maintainer = new DuplicateGroupMaintainer(bed.Get<ILibrarySession>(), TimeProvider.System, NullLogger<DuplicateGroupMaintainer>.Instance, delay: TimeSpan.FromMilliseconds(20));
        maintainer.Start();
    }

    public async ValueTask DisposeAsync()
    {
        maintainer.Dispose();
        await bed.DisposeAsync();
    }

    [Fact]
    public async Task Import_CopyOfAnExistingFile_CreatesThenExtendsItsGroup()
    {
        bed.WriteDistinctPhotos(3);
        await ImportAndSettleAsync();
        Assert.Equal(0, await bed.Library.Duplicates.CountAsync(null, Token));

        var original = Path.Combine(bed.Root, "photos", "IMG_00001.jpg");
        bed.Copy(original, "copies/first.jpg");
        await ImportAndSettleAsync();

        var group = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(DuplicateGroupType.Exact, group.Type);
        Assert.Equal(["copies/first.jpg", "photos/IMG_00001.jpg"], await MemberPathsAsync(group));

        await bed.Library.Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, DateTimeOffset.UtcNow, Token);
        bed.Copy(original, "copies/second.jpg");
        await ImportAndSettleAsync();

        var extended = Assert.Single(await bed.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        Assert.Equal(group.Id, extended.Id);
        Assert.Equal(DuplicateGroupState.Open, extended.State);
        Assert.Equal(["copies/first.jpg", "copies/second.jpg", "photos/IMG_00001.jpg"], await MemberPathsAsync(extended));
    }

    private async Task ImportAndSettleAsync()
    {
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        await maintainer.IdleAsync();
    }

    private async Task<List<string>> MemberPathsAsync(DuplicateGroup group)
    {
        var members = await bed.Library.Duplicates.GetMembersAsync(group.Id, Token);
        var items = await bed.Library.Items.GetByIdsAsync([.. members.Select(member => member.ItemId)], Token);
        return [.. items.Select(item => item.Path.Value).Order(StringComparer.Ordinal)];
    }
}
