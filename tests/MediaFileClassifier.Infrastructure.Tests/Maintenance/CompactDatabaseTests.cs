using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.UseCases.Maintenance.CompactDatabase;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Maintenance;

/// <summary>
/// "Compact database" on a real library (MFC-1022): the space query, the refusals and the VACUUM itself.
/// </summary>
public sealed class CompactDatabaseTests : IAsyncLifetime
{
    private readonly LibrarySession session = new();
    private readonly StubJobSystem jobs = new();
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        await session.SetCurrentAsync(bed.Library);
    }

    public async ValueTask DisposeAsync()
    {
        await session.DisposeAsync();
        await bed.DisposeAsync();
    }

    [Fact]
    public async Task Compact_AfterALargeDelete_ShrinksTheFile_AndKeepsTheData()
    {
        await FillAndEmptyAsync();

        var result = await Handler().Handle(new CompactDatabaseCommand(), Token);

        Assert.Equal(CompactDatabaseOutcome.Compacted, result.Outcome);
        Assert.True(result.After!.FileBytes < result.Before!.FileBytes / 2, $"{result.Before.FileBytes} -> {result.After.FileBytes}");
        Assert.Equal(100, await bed.Library.Items.CountAsync(Token));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
    }

    [Fact]
    public async Task Compact_FreshLibrary_HasNothingWorthReclaiming()
    {
        var result = await Handler().Handle(new CompactDatabaseCommand(), Token);

        Assert.Equal(CompactDatabaseOutcome.NothingToReclaim, result.Outcome);
        Assert.Null(result.After);
    }

    [Fact]
    public async Task Compact_WhileJobsHaveWork_RefusesAndLeavesTheFileAlone()
    {
        await FillAndEmptyAsync();
        var before = await bed.Library.Maintenance.GetSpaceAsync(Token);
        jobs.Pending = 3;

        var result = await Handler().Handle(new CompactDatabaseCommand(), Token);

        Assert.Equal(CompactDatabaseOutcome.JobsRunning, result.Outcome);
        Assert.Equal(before.FileBytes, (await bed.Library.Maintenance.GetSpaceAsync(Token)).FileBytes);
    }

    [Fact]
    public async Task Compact_WithoutALibrary_SaysSo()
    {
        await session.SetCurrentAsync(null);

        var result = await Handler().Handle(new CompactDatabaseCommand(), Token);

        Assert.Equal(CompactDatabaseOutcome.NoLibrary, result.Outcome);
        Assert.Null(await new GetDatabaseSpaceQueryHandler(session).Handle(new GetDatabaseSpaceQuery(), Token));
    }

    [Fact]
    public async Task Space_OfAnOpenLibrary_ReportsTheFileAndTheDisk()
    {
        var space = await new GetDatabaseSpaceQueryHandler(session).Handle(new GetDatabaseSpaceQuery(), Token);

        Assert.NotNull(space);
        Assert.True(space.FileBytes > 0);
        Assert.InRange(space.FreeBytes, 0, space.FileBytes);
        Assert.True(space.AvailableDiskBytes > 0);
    }

    [Theory]
    [InlineData(100, 50, true)]
    [InlineData(100, 5, false)] // Under 4 MiB free.
    [InlineData(1000, 80, false)] // Over 4 MiB, but under 10% of the file.
    [InlineData(1000, 100, true)]
    public void IsWorthwhile_NeedsEnoughFreeSpace_InBytesAndShare(long fileMiB, long freeMiB, bool expected)
    {
        Assert.Equal(expected, DatabaseCompaction.IsWorthwhile(new DatabaseSpace(fileMiB * 1024 * 1024, freeMiB * 1024 * 1024)));
    }

    [Fact]
    public void RequiredDiskBytes_IsTwiceTheData()
    {
        Assert.Equal(160, DatabaseCompaction.RequiredDiskBytes(new DatabaseSpace(100, 20)));
    }

    private CompactDatabaseCommandHandler Handler() => new(session, NullLogger<CompactDatabaseCommandHandler>.Instance, jobs);

    private async Task FillAndEmptyAsync()
    {
        // Enough rows that the freed pages pass the 4 MiB threshold.
        var items = Enumerable.Range(1, 30_000).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        await bed.Library.Items.DeleteFromFolderAsync(bed.Folder.Id, int.MaxValue, Token);
        await bed.Library.Items.AddRangeAsync([.. Enumerable.Range(100_001, 100).Select(bed.NewItem)], Token);
    }

    private sealed class StubJobSystem : IJobSystem
    {
        public int Pending { get; set; }

        public IJobScheduler? Current => null;

        public Task<IJobScheduler> StartAsync(IOpenLibrary library, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync() => Task.CompletedTask;

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(Pending);
    }
}
