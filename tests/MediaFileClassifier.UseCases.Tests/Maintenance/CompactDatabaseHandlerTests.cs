using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Maintenance.CompactDatabase;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Maintenance;

/// <summary>
/// The refusals and failure paths of "Compact database" (MFC-1022), with a scripted maintenance service.
/// </summary>
public sealed class CompactDatabaseHandlerTests
{
    private const long Mib = 1024 * 1024;

    private readonly FakeLibrarySession session = new();
    private readonly ScriptedMaintenance maintenance = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_DiskTooSmallForTheRebuiltFile_RefusesWithoutVacuuming()
    {
        // 100 MiB file, 60 MiB free: 40 MiB of data needs 80 MiB of room.
        maintenance.Space = new DatabaseSpace(100 * Mib, 60 * Mib, AvailableDiskBytes: 79 * Mib);

        var result = await HandleAsync();

        Assert.Equal(CompactDatabaseOutcome.NotEnoughDiskSpace, result.Outcome);
        Assert.Equal(0, maintenance.Vacuums);
    }

    [Fact]
    public async Task Handle_DiskSizeUnknown_StillCompacts()
    {
        maintenance.Space = new DatabaseSpace(100 * Mib, 60 * Mib, AvailableDiskBytes: null);

        var result = await HandleAsync();

        Assert.Equal(CompactDatabaseOutcome.Compacted, result.Outcome);
        Assert.Equal(1, maintenance.Vacuums);
    }

    [Fact]
    public async Task Handle_VacuumFails_ReportsFailedInsteadOfThrowing()
    {
        maintenance.VacuumFailure = new IOException("There is not enough space on the disk.");

        var result = await HandleAsync();

        Assert.Equal(CompactDatabaseOutcome.Failed, result.Outcome);
        Assert.Null(result.After);
    }

    [Fact]
    public async Task Handle_ReadingTheNewSizeFails_IsStillCompacted()
    {
        maintenance.FailSpaceAfterVacuum = true;

        var result = await HandleAsync();

        Assert.Equal(CompactDatabaseOutcome.Compacted, result.Outcome);
        Assert.Equal(40 * Mib, result.After!.FileBytes);
    }

    [Fact]
    public async Task Handle_VacuumCancelledWhileQueued_IsNotAFailure()
    {
        // A queued vacuum that's cancelled isn't a failure: the cancellation reaches the caller, which ends quietly.
        maintenance.VacuumFailure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => HandleAsync());
    }

    private async Task<CompactDatabaseResult> HandleAsync(CancellationToken? token = null)
    {
        await session.SetCurrentAsync(new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "compact"), "compact") { MaintenanceService = maintenance });
        return await new CompactDatabaseCommandHandler(session, NullLogger<CompactDatabaseCommandHandler>.Instance)
            .Handle(new CompactDatabaseCommand(), token ?? Token);
    }

    private sealed class ScriptedMaintenance : ILibraryMaintenance
    {
        public DatabaseSpace Space { get; set; } = new(100 * Mib, 60 * Mib, AvailableDiskBytes: 10_000 * Mib);

        public Exception? VacuumFailure { get; set; }

        public bool FailSpaceAfterVacuum { get; set; }

        public int Vacuums { get; private set; }

        public Task<DatabaseSpace> GetSpaceAsync(CancellationToken cancellationToken = default) =>
            Vacuums > 0 && FailSpaceAfterVacuum
                ? throw new InvalidOperationException("The database is busy.")
                : Task.FromResult(Vacuums > 0 ? new DatabaseSpace(40 * Mib, 0) : Space);

        public Task OptimizeAsync(bool vacuum = false, CancellationToken cancellationToken = default)
        {
            if (VacuumFailure is { } failure)
            {
                return Task.FromException(failure);
            }

            Vacuums++;
            return Task.CompletedTask;
        }

        public Task<IntegrityCheckResult> CheckIntegrityAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
