using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.OpenLibrary;

/// <summary>
/// Opens a library: checks the folder, takes the lock, migrates an older schema after a backup, makes it the open
/// library and puts it first in the recent list (E02-S02-T01).
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
public sealed class OpenLibraryCommandHandler(
    ILibraryStore store,
    ILibrarySession session,
    ISettingsService settings,
    TimeProvider timeProvider,
    ILogger<OpenLibraryCommandHandler> logger) : IRequestHandler<OpenLibraryCommand, OpenLibraryResult>
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
        RecentLibraries.Record(settings, library, timeProvider.GetUtcNow());
        return new OpenLibraryResult(library.Layout.RootDirectory, Error: null);
    }
}
