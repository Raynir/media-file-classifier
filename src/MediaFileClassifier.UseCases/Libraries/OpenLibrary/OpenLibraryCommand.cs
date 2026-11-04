using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.OpenLibrary;

/// <summary>
/// Opens a library: checks the folder, takes the lock, migrates an older schema after a backup, makes it the open
/// library and puts it first in the recent list (E02-S02-T01). Then its folders are checked, so the unreachable ones
/// show as Offline (E02-S03-T05).
/// </summary>
/// <param name="LibraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
/// <param name="Progress">Told when a migration starts, or <c>null</c>.</param>
public sealed record OpenLibraryCommand(string LibraryDirectory, IProgress<LibraryOpenStage>? Progress = null) : IRequest<OpenLibraryResult>;

/// <summary>
/// Outcome of opening a library.
/// </summary>
/// <param name="LibraryDirectory">The library folder.</param>
/// <param name="Error">Why it didn't open, or <c>null</c> if it did.</param>
/// <param name="BackupPath">After <see cref="LibraryOpenError.MigrationFailed"/>, the backup that can be restored.</param>
public sealed record OpenLibraryResult(string LibraryDirectory, LibraryOpenError? Error, string? BackupPath = null)
{
    /// <summary>
    /// Gets a value indicating whether the library is open.
    /// </summary>
    public bool Succeeded => Error is null;
}

/// <summary>
/// Handles <see cref="OpenLibraryCommand"/>.
/// </summary>
/// <param name="store">Opens library folders.</param>
/// <param name="session">Holds the open library.</param>
/// <param name="settings">App settings, with the recent-libraries list.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
/// <param name="sender">Checks the folders, or <c>null</c> to skip that.</param>
public sealed class OpenLibraryCommandHandler(
    ILibraryStore store,
    ILibrarySession session,
    ISettingsService settings,
    TimeProvider timeProvider,
    ILogger<OpenLibraryCommandHandler> logger,
    ISender? sender = null) : IRequestHandler<OpenLibraryCommand, OpenLibraryResult>
{
    /// <inheritdoc />
    public async Task<OpenLibraryResult> Handle(OpenLibraryCommand request, CancellationToken cancellationToken)
    {
        var directory = request.LibraryDirectory;
        if (session.Current is { } current && RecentLibraries.SamePath(current.Layout.RootDirectory, directory))
        {
            return new OpenLibraryResult(directory, Error: null);
        }

        IOpenLibrary library;
        try
        {
            library = await store.OpenAsync(directory, request.Progress, cancellationToken).ConfigureAwait(false);
        }
        catch (LibraryOpenException ex)
        {
            logger.LogWarning(ex, "Couldn't open library {Library}: {Error}", directory, ex.Error);
            return new OpenLibraryResult(directory, ex.Error, ex.BackupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't open library {Library}", directory);
            return new OpenLibraryResult(directory, LibraryOpenError.NotWritable);
        }

        logger.LogInformation("Opened library {Library}", library.Layout.RootDirectory);
        await session.SetCurrentAsync(library).ConfigureAwait(false);
        try
        {
            RecentLibraries.Record(settings, library, timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The library is open; only the recent list misses it.
            logger.LogWarning(ex, "Couldn't add library {Library} to the recent libraries", library.Layout.RootDirectory);
        }

        if (sender is not null)
        {
            try
            {
                await sender.Send(new CheckFolderAvailabilityCommand(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Couldn't check the folders of {Library}", library.Layout.RootDirectory);
            }
        }

        return new OpenLibraryResult(library.Layout.RootDirectory, Error: null);
    }
}
