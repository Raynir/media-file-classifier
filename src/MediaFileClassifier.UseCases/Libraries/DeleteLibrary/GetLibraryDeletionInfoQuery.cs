using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Libraries.DeleteLibrary;

/// <summary>
/// Gets what the delete confirmation shows about a library (E02-S07): its name and item count, and whether it's the
/// open library or open in another app instance.
/// </summary>
/// <param name="LibraryDirectory">The <c>&lt;name&gt;.mfc</c> folder.</param>
public sealed record GetLibraryDeletionInfoQuery(string LibraryDirectory) : IRequest<LibraryDeletionInfo>;

/// <summary>
/// What the delete confirmation shows.
/// </summary>
/// <param name="LibraryDirectory">The library folder.</param>
/// <param name="Name">The library's name, or <c>null</c> when it couldn't be read.</param>
/// <param name="ItemCount">How many items the library has indexed.</param>
/// <param name="IsOpen">Whether it's the library open in this app instance, which deleting closes.</param>
/// <param name="Error">Why it can't be deleted, e.g. <see cref="LibraryOpenError.InUse"/> when another instance has it
/// open, or <c>null</c> if it can.</param>
public sealed record LibraryDeletionInfo(string LibraryDirectory, string? Name, int ItemCount, bool IsOpen, LibraryOpenError? Error);

/// <summary>
/// Handles <see cref="GetLibraryDeletionInfoQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="deleter">Reads a closed library.</param>
public sealed class GetLibraryDeletionInfoQueryHandler(ILibrarySession session, ILibraryDeleter deleter)
    : IRequestHandler<GetLibraryDeletionInfoQuery, LibraryDeletionInfo>
{
    /// <inheritdoc />
    public async Task<LibraryDeletionInfo> Handle(GetLibraryDeletionInfoQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var directory = request.LibraryDirectory;
        if (DeleteLibraryCommandHandler.IsOpen(session, directory) is { } open)
        {
            var count = await open.Items.CountAsync(cancellationToken).ConfigureAwait(false);
            return new LibraryDeletionInfo(directory, open.Library.Name, count, IsOpen: true, Error: null);
        }

        try
        {
            var preview = await deleter.InspectAsync(directory, cancellationToken).ConfigureAwait(false);
            return new LibraryDeletionInfo(directory, preview.Name, preview.ItemCount, IsOpen: false, Error: null);
        }
        catch (LibraryOpenException exception)
        {
            return new LibraryDeletionInfo(directory, Name: null, ItemCount: 0, IsOpen: false, exception.Error);
        }
    }
}
