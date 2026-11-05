using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores keyframes.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class KeyframeRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IKeyframeRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Keyframe>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<Keyframe>>(
            async (db, token) => await db.Keyframes.Where(keyframe => keyframe.ItemId == itemId).OrderBy(keyframe => keyframe.Index)
                .ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task ReplaceAsync(Id<MediaItem> itemId, IReadOnlyList<Keyframe> keyframes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        if (keyframes.Any(keyframe => keyframe.ItemId != itemId))
        {
            throw new ArgumentException($"Every keyframe must belong to item {itemId}.", nameof(keyframes));
        }

        if (keyframes.DistinctBy(keyframe => keyframe.Index).Count() != keyframes.Count)
        {
            throw new ArgumentException("Two keyframes have the same index.", nameof(keyframes));
        }

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                // The database cascades the delete to per-frame embeddings.
                await db.Keyframes.Where(keyframe => keyframe.ItemId == itemId).ExecuteDeleteAsync(token).ConfigureAwait(false);
                db.Keyframes.AddRange(keyframes);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
    }
}
