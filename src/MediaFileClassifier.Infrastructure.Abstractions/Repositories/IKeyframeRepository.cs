using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores the keyframes sampled from videos and animated images (E06-S02-T01). Per-frame results, such as
/// embeddings, reference a keyframe and are deleted with it.
/// </summary>
public interface IKeyframeRepository
{
    /// <summary>
    /// Gets an item's keyframes.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The keyframes, in index order; empty when the item has none.</returns>
    Task<IReadOnlyList<Keyframe>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an item's keyframes in one write. The old keyframes are deleted with everything that references them,
    /// such as per-frame embeddings, because they describe frames of an earlier sampling.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="keyframes">The new keyframes, all of <paramref name="itemId"/>, with distinct indexes. Empty
    /// removes them all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the keyframes are written.</returns>
    /// <exception cref="ArgumentException">A keyframe belongs to another item, or two have the same index.</exception>
    Task ReplaceAsync(Id<MediaItem> itemId, IReadOnlyList<Keyframe> keyframes, CancellationToken cancellationToken = default);
}
