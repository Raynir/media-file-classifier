using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.OpenLibrary;

/// <summary>
/// Puts back the database backup taken before a failed migration (E02-S02-T05).
/// </summary>
/// <param name="LibraryDirectory">The library folder.</param>
/// <param name="BackupPath">The backup, from <see cref="OpenLibraryResult.BackupPath"/>.</param>
public sealed record RestoreLibraryBackupCommand(string LibraryDirectory, string BackupPath) : IRequest<bool>;

/// <summary>
/// Handles <see cref="RestoreLibraryBackupCommand"/>.
/// </summary>
/// <param name="store">Restores the backup.</param>
/// <param name="logger">Logger.</param>
public sealed class RestoreLibraryBackupCommandHandler(ILibraryStore store, ILogger<RestoreLibraryBackupCommandHandler> logger)
    : IRequestHandler<RestoreLibraryBackupCommand, bool>
{
    /// <inheritdoc />
    /// <returns><c>true</c> if the backup is back in place.</returns>
    public async Task<bool> Handle(RestoreLibraryBackupCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await store.RestoreBackupAsync(request.LibraryDirectory, request.BackupPath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is LibraryOpenException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't restore library {Library} from {BackupPath}", request.LibraryDirectory, request.BackupPath);
            return false;
        }
    }
}
