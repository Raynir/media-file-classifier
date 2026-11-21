using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Libraries.OpenLibrary;
using MediaFileClassifier.UseCases.Maintenance.CheckIntegrity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Maintenance.RestoreBackup;

/// <summary>
/// Restores a backup over the open library's damaged database, after the integrity check found it damaged
/// (E02-S06-T03): checks the backup, closes the library, keeps the damaged database in the backups folder, puts the
/// backup in its place, opens the library again (which migrates it) and checks it. Once the library is closed it
/// isn't cancelled, so the library is never left closed half-way.
/// </summary>
/// <param name="Backup">The backup, from <see cref="IntegrityReport.Backup"/>.</param>
public sealed record RestoreIntegrityBackupCommand(LibraryBackup Backup) : IRequest<RestoreIntegrityBackupResult>;

/// <summary>
/// Why a backup wasn't restored.
/// </summary>
public enum RestoreBackupFailure
{
    /// <summary>
    /// No library is open.
    /// </summary>
    NoLibrary,

    /// <summary>
    /// The backup failed its own integrity check. Nothing was changed.
    /// </summary>
    BackupDamaged,

    /// <summary>
    /// The database files couldn't be moved. They were put back and the library opened again.
    /// </summary>
    NotReplaced,

    /// <summary>
    /// The restored library didn't open. The damaged database was put back and the library opened again.
    /// </summary>
    OpenFailed,

    /// <summary>
    /// The restored library didn't open, and the damaged database couldn't be put back either. It stays in the
    /// backups folder, at <see cref="RestoreIntegrityBackupResult.DamagedPath"/>.
    /// </summary>
    NotPutBack,
}

/// <summary>
/// The result of <see cref="RestoreIntegrityBackupCommand"/>.
/// </summary>
/// <param name="Failure">Why it wasn't restored, or <c>null</c> when it was.</param>
/// <param name="Report">After a restore, the integrity check of the restored library.</param>
/// <param name="DamagedPath">Where the damaged database is kept, once it was moved.</param>
public sealed record RestoreIntegrityBackupResult(RestoreBackupFailure? Failure, IntegrityReport? Report = null, string? DamagedPath = null)
{
    /// <summary>
    /// Gets a value indicating whether the backup was restored.
    /// </summary>
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Handles <see cref="RestoreIntegrityBackupCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="store">Checks the backup and swaps the database files.</param>
/// <param name="sender">Opens the library again and checks it.</param>
/// <param name="logger">Logger.</param>
public sealed class RestoreIntegrityBackupCommandHandler(
    ILibrarySession session,
    ILibraryStore store,
    ISender sender,
    ILogger<RestoreIntegrityBackupCommandHandler> logger) : IRequestHandler<RestoreIntegrityBackupCommand, RestoreIntegrityBackupResult>
{
    /// <inheritdoc />
    public async Task<RestoreIntegrityBackupResult> Handle(RestoreIntegrityBackupCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library)
        {
            return new RestoreIntegrityBackupResult(RestoreBackupFailure.NoLibrary);
        }

        var directory = library.Layout.RootDirectory;
        var backup = request.Backup.Path;
        var check = await store.CheckBackupAsync(backup, cancellationToken).ConfigureAwait(false);
        if (!check.IsOk)
        {
            logger.LogError(
                "The backup {BackupPath} of {Library} is damaged too: {Problems}",
                backup,
                directory,
                string.Join(" | ", check.Problems.Take(CheckLibraryIntegrityQueryHandler.LoggedProblems)));
            return new RestoreIntegrityBackupResult(RestoreBackupFailure.BackupDamaged);
        }

        // From here on the library is closed, so the steps run to the end whatever the token says.
        await session.SetCurrentAsync(null).ConfigureAwait(false);
        string damagedPath;
        try
        {
            damagedPath = await store.ReplaceDamagedDatabaseAsync(directory, backup, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is LibraryOpenException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't put the backup {BackupPath} in place in {Library}", backup, directory);
            await ReopenAsync(directory).ConfigureAwait(false);
            return new RestoreIntegrityBackupResult(RestoreBackupFailure.NotReplaced);
        }

        var opened = await sender.Send(new OpenLibraryCommand(directory), CancellationToken.None).ConfigureAwait(false);
        if (!opened.Succeeded)
        {
            logger.LogError("The library {Library} didn't open after restoring {BackupPath}: {Error}", directory, backup, opened.Error);
            try
            {
                await store.PutBackDamagedDatabaseAsync(directory, damagedPath, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is LibraryOpenException or IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Couldn't put the damaged database {DamagedPath} back into {Library}", damagedPath, directory);
                return new RestoreIntegrityBackupResult(RestoreBackupFailure.NotPutBack, DamagedPath: damagedPath);
            }

            await ReopenAsync(directory).ConfigureAwait(false);
            return new RestoreIntegrityBackupResult(RestoreBackupFailure.OpenFailed);
        }

        var report = await sender.Send(new CheckLibraryIntegrityQuery(), CancellationToken.None).ConfigureAwait(false);
        return new RestoreIntegrityBackupResult(Failure: null, report, damagedPath);
    }

    private async Task ReopenAsync(string directory)
    {
        var reopened = await sender.Send(new OpenLibraryCommand(directory), CancellationToken.None).ConfigureAwait(false);
        if (!reopened.Succeeded)
        {
            logger.LogError("Couldn't open the library {Library} again after a failed restore: {Error}", directory, reopened.Error);
        }
    }
}
