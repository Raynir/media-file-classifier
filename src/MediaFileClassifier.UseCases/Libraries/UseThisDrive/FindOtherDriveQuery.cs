using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Libraries.UseThisDrive;

/// <summary>
/// Tells whether an Offline folder's root leads to another drive (MFC-1051), so the folders dialog can offer
/// <see cref="UseThisDriveCommand"/>. It reads the drive, so it's asked when a folder is selected, not on every refresh.
/// </summary>
/// <param name="FolderId">The folder.</param>
public sealed record FindOtherDriveQuery(Id<IndexedFolder> FolderId) : IRequest<OtherDrive?>;

/// <summary>
/// Handles <see cref="FindOtherDriveQuery"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
/// <param name="files">Lists the folder's root and reads its label.</param>
/// <param name="logger">Logger.</param>
/// <param name="volumes">Reads the drive's identity, or <see langword="null"/> where the platform has none.</param>
public sealed class FindOtherDriveQueryHandler(
    ILibrarySession session,
    IMediaFileSource files,
    ILogger<FindOtherDriveQueryHandler> logger,
    IVolumeLocator? volumes = null) : IRequestHandler<FindOtherDriveQuery, OtherDrive?>
{
    /// <inheritdoc />
    /// <returns>The other drive, or <see langword="null"/> when there's none, no library is open or the folder is
    /// gone.</returns>
    public async Task<OtherDrive?> Handle(FindOtherDriveQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (session.Current is not { } library
            || await library.Folders.GetAsync(request.FolderId, cancellationToken).ConfigureAwait(false) is not { } folder)
        {
            return null;
        }

        return await OtherDrive.FindAsync(files, volumes, folder, logger, cancellationToken).ConfigureAwait(false);
    }
}
