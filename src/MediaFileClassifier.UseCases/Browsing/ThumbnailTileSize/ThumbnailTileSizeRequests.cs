using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Browsing.ThumbnailTileSize;

/// <summary>
/// Gets the open library's last thumbnail tile width (E14-S01-T05, D050), or <see langword="null"/> when no library is
/// open.
/// </summary>
public sealed record GetThumbnailTileSizeQuery : IRequest<int?>;

/// <summary>
/// Remembers the thumbnail tile width in the open library (E14-S01-T05, D050). Does nothing when no library is open
/// or the width didn't change.
/// </summary>
/// <param name="Size">Tile width in device-independent pixels.</param>
public sealed record SetThumbnailTileSizeCommand(int Size) : IRequest;

/// <summary>
/// Handles <see cref="GetThumbnailTileSizeQuery"/> and <see cref="SetThumbnailTileSizeCommand"/>.
/// </summary>
/// <param name="session">Holds the open library.</param>
public sealed class ThumbnailTileSizeHandlers(ILibrarySession session)
    : IRequestHandler<GetThumbnailTileSizeQuery, int?>, IRequestHandler<SetThumbnailTileSizeCommand>
{
    /// <inheritdoc />
    public Task<int?> Handle(GetThumbnailTileSizeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(session.Current?.Library.Settings.ThumbnailTileSize);

    /// <inheritdoc />
    public async Task Handle(SetThumbnailTileSizeCommand request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Size);

        if (session.Current is not { } open || open.Library.Settings.ThumbnailTileSize == request.Size)
        {
            return;
        }

        // The same record the library settings service reads, so both see the change.
        await open.UpdateSettingsAsync(settings => settings with { ThumbnailTileSize = request.Size }, cancellationToken).ConfigureAwait(false);
    }
}
