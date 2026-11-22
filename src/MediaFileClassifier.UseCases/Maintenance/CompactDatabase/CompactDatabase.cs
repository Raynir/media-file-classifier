using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Maintenance.CompactDatabase;

/// <summary>
/// When compacting the database is worth its time (MFC-1022, drafted decision): the free pages are at least
/// <see cref="MinFreeBytes"/> and <see cref="MinFreeShare"/> of the file. A smaller gain isn't worth rewriting the file.
/// </summary>
public static class DatabaseCompaction
{
    /// <summary>
    /// The least free space that makes compacting worthwhile.
    /// </summary>
    public const long MinFreeBytes = 4L * 1024 * 1024;

    /// <summary>
    /// The least share of the file that must be free, from 0 to 1.
    /// </summary>
    public const double MinFreeShare = 0.10;

    /// <summary>
    /// Tells whether compacting would give back enough space.
    /// </summary>
    /// <param name="space">The database's sizes.</param>
    /// <returns><c>true</c> when compacting is worthwhile.</returns>
    public static bool IsWorthwhile(DatabaseSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);

        return space.FreeBytes >= MinFreeBytes && space.FreeBytes >= space.FileBytes * MinFreeShare;
    }

    /// <summary>
    /// Gets the free disk space a compaction needs: the rebuilt file and its copy in the WAL, so twice the data.
    /// </summary>
    /// <param name="space">The database's sizes.</param>
    /// <returns>The bytes.</returns>
    public static long RequiredDiskBytes(DatabaseSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);

        return space.UsedBytes * 2;
    }
}

/// <summary>
/// Reads how much space compacting the open library's database would give back.
/// </summary>
public sealed record GetDatabaseSpaceQuery : IRequest<DatabaseSpace?>;

/// <summary>
/// Handles <see cref="GetDatabaseSpaceQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class GetDatabaseSpaceQueryHandler(ILibrarySession session) : IRequestHandler<GetDatabaseSpaceQuery, DatabaseSpace?>
{
    /// <inheritdoc />
    public async Task<DatabaseSpace?> Handle(GetDatabaseSpaceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return session.Current is { } library
            ? await library.Maintenance.GetSpaceAsync(cancellationToken).ConfigureAwait(false)
            : null;
    }
}

/// <summary>
/// Compacts the open library's database with <c>VACUUM</c> (MFC-1022), so a file that grew before a big cleanup gives
/// the free space back to the disk. It doesn't start while jobs have work left, because the vacuum holds the writer.
/// </summary>
public sealed record CompactDatabaseCommand : IRequest<CompactDatabaseResult>;

/// <summary>
/// Why a database wasn't compacted, or that it was.
/// </summary>
public enum CompactDatabaseOutcome
{
    /// <summary>
    /// The database was compacted.
    /// </summary>
    Compacted,

    /// <summary>
    /// No library is open.
    /// </summary>
    NoLibrary,

    /// <summary>
    /// Jobs still have work to do. Nothing was changed.
    /// </summary>
    JobsRunning,

    /// <summary>
    /// The free space isn't worth reclaiming (<see cref="DatabaseCompaction.IsWorthwhile"/>). Nothing was changed.
    /// </summary>
    NothingToReclaim,

    /// <summary>
    /// The drive has too little room for the rebuilt file. Nothing was changed.
    /// </summary>
    NotEnoughDiskSpace,
}

/// <summary>
/// The result of <see cref="CompactDatabaseCommand"/>.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Before">The database's sizes before, when a library was open.</param>
/// <param name="After">The database's sizes after a compaction.</param>
public sealed record CompactDatabaseResult(CompactDatabaseOutcome Outcome, DatabaseSpace? Before = null, DatabaseSpace? After = null);

/// <summary>
/// Handles <see cref="CompactDatabaseCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="logger">Logger.</param>
/// <param name="jobs">The job system, or <c>null</c> where there's none.</param>
public sealed class CompactDatabaseCommandHandler(
    ILibrarySession session,
    ILogger<CompactDatabaseCommandHandler> logger,
    IJobSystem? jobs = null) : IRequestHandler<CompactDatabaseCommand, CompactDatabaseResult>
{
    // One compaction at a time, even when the command is sent twice.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <inheritdoc />
    public async Task<CompactDatabaseResult> Handle(CompactDatabaseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return new CompactDatabaseResult(CompactDatabaseOutcome.NoLibrary);
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (jobs is not null && await jobs.CountPendingAsync(cancellationToken).ConfigureAwait(false) > 0)
            {
                return new CompactDatabaseResult(CompactDatabaseOutcome.JobsRunning);
            }

            var before = await library.Maintenance.GetSpaceAsync(cancellationToken).ConfigureAwait(false);
            if (!DatabaseCompaction.IsWorthwhile(before))
            {
                return new CompactDatabaseResult(CompactDatabaseOutcome.NothingToReclaim, before);
            }

            if (before.AvailableDiskBytes is { } available && available < DatabaseCompaction.RequiredDiskBytes(before))
            {
                logger.LogWarning(
                    "Not compacting {Library}: {Available} bytes of disk are free, {Required} are needed",
                    library.Library.Name,
                    available,
                    DatabaseCompaction.RequiredDiskBytes(before));
                return new CompactDatabaseResult(CompactDatabaseOutcome.NotEnoughDiskSpace, before);
            }

            var started = Stopwatch.GetTimestamp();

            // Once it starts it runs to the end: SQLite can't stop a VACUUM half-way, so the token only stops a queued one.
            await library.Maintenance.OptimizeAsync(vacuum: true, cancellationToken).ConfigureAwait(false);
            var after = await library.Maintenance.GetSpaceAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation(
                "Compacted {Library} from {Before} to {After} bytes in {ElapsedMs} ms",
                library.Library.Name,
                before.FileBytes,
                after.FileBytes,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new CompactDatabaseResult(CompactDatabaseOutcome.Compacted, before, after);
        }
        finally
        {
            Gate.Release();
        }
    }
}
