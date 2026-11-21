using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.DeleteLibrary;

/// <summary>
/// Deletes a library (E02-S07-T01): closes it if it's the open one, deletes its own files and then its folder if
/// nothing else is left, and removes it from the recent list. Media files and anything else in the folder stay.
/// </summary>
/// <param name="LibraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
public sealed record DeleteLibraryCommand(string LibraryDirectory) : IRequest<DeleteLibraryResult>;

/// <summary>
/// What deleting a library did.
/// </summary>
/// <param name="Error">Why nothing was deleted, or <c>null</c> when the library was deleted.</param>
/// <param name="FolderRemoved">Whether the library folder was removed too.</param>
/// <param name="KeptEntries">The entries left in the library folder, relative to it.</param>
public sealed record DeleteLibraryResult(LibraryOpenError? Error, bool FolderRemoved, IReadOnlyList<string> KeptEntries)
{
    /// <summary>
    /// Gets a value indicating whether the library was deleted.
    /// </summary>
    public bool Deleted => Error is null;
}

/// <summary>
/// Handles <see cref="DeleteLibraryCommand"/>.
/// </summary>
/// <param name="session">Holds the open library, which is closed first.</param>
/// <param name="deleter">Deletes the library's files.</param>
/// <param name="settings">App settings, with the recent list.</param>
/// <param name="logger">Logger.</param>
public sealed class DeleteLibraryCommandHandler(
    ILibrarySession session,
    ILibraryDeleter deleter,
    ISettingsService settings,
    ILogger<DeleteLibraryCommandHandler> logger) : IRequestHandler<DeleteLibraryCommand, DeleteLibraryResult>
{
    /// <inheritdoc />
    public async Task<DeleteLibraryResult> Handle(DeleteLibraryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsOpen(session, request.LibraryDirectory) is not null)
        {
            // Stops background work and releases the lock; the deleter takes the lock again.
            await session.SetCurrentAsync(null).ConfigureAwait(false);
        }

        LibraryDeletionResult result;
        try
        {
            result = await deleter.DeleteAsync(request.LibraryDirectory, CancellationToken.None).ConfigureAwait(false);
        }
        catch (LibraryOpenException exception)
        {
            logger.LogWarning(exception, "Couldn't delete library {Directory}: {Error}", request.LibraryDirectory, exception.Error);
            return new DeleteLibraryResult(exception.Error, FolderRemoved: false, KeptEntries: []);
        }

        RecentLibraries.Remove(settings, Path.GetFullPath(request.LibraryDirectory));
        return new DeleteLibraryResult(Error: null, result.FolderRemoved, result.KeptEntries);
    }

    /// <summary>
    /// Gets the open library if it's the one in a folder.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="libraryDirectory">The library folder.</param>
    /// <returns>The open library, or <c>null</c> if another one or none is open.</returns>
    internal static IOpenLibrary? IsOpen(ILibrarySession session, string libraryDirectory) =>
        session.Current is { } open && RecentLibraries.SamePath(open.Layout.RootDirectory, Path.GetFullPath(libraryDirectory)) ? open : null;
}
