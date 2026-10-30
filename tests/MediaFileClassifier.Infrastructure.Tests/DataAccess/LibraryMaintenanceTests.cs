using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryMaintenance"/>: integrity check, online backup and optimization (E01-S03-T07).
/// </summary>
public sealed class LibraryMaintenanceTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task CheckIntegrityAsync_NewLibrary_IsOk()
    {
        var result = await bed.Library.Maintenance.CheckIntegrityAsync(Token);

        Assert.True(result.IsOk);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task BackupAsync_DuringWriteLoad_CanBeOpenedAsLibraryAndPassesIntegrityCheck()
    {
        var ingestion = Task.Run(() => bed.Library.Items.AddRangeAsync([.. Enumerable.Range(1, 10_000).Select(bed.NewItem)], Token), Token);
        while (await bed.Library.Items.CountAsync(Token) == 0)
        {
            await Task.Delay(5, Token);
        }

        var restored = Path.Combine(bed.Temporary.ParentDirectory, "restored" + LibraryLayout.Extension);
        await bed.Library.Maintenance.BackupAsync(Path.Combine(restored, LibraryLayout.DatabaseFileName), Token);
        var writtenDuringBackup = !ingestion.IsCompleted;
        await ingestion;

        await using var copy = await bed.Store.OpenAsync(restored, Token);
        Assert.True((await copy.Maintenance.CheckIntegrityAsync(Token)).IsOk);
        var count = await copy.Items.CountAsync(Token);
        Assert.InRange(count, 1, 10_000);
        Assert.Equal(bed.Library.Library.Id, copy.Library.Id);
        TestContext.Current.TestOutputHelper?.WriteLine($"Backup holds {count} items; ingestion still running after backup: {writtenDuringBackup}.");
    }

    [Fact]
    public async Task BackupAsync_DestinationExists_Throws()
    {
        var destination = Path.Combine(bed.Temporary.ParentDirectory, "backup.db");
        await File.WriteAllTextAsync(destination, "keep me", Token);

        await Assert.ThrowsAsync<IOException>(() => bed.Library.Maintenance.BackupAsync(destination, Token));
        Assert.Equal("keep me", await File.ReadAllTextAsync(destination, Token));
    }

    [Fact]
    public async Task OptimizeAsync_WithVacuum_KeepsDataIntact()
    {
        var items = Enumerable.Range(1, 200).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);
        foreach (var item in items.Take(100))
        {
            await bed.Library.Items.DeleteAsync(item.Id, Token);
        }

        await bed.Library.Maintenance.OptimizeAsync(vacuum: true, Token);

        Assert.Equal(100, await bed.Library.Items.CountAsync(Token));
        Assert.True((await bed.Library.Maintenance.CheckIntegrityAsync(Token)).IsOk);
    }
}
