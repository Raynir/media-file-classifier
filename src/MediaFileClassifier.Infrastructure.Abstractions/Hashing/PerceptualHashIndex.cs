using System.Numerics;

namespace MediaFileClassifier.Infrastructure.Abstractions.Hashing;

/// <summary>
/// Finds perceptual hashes within a Hamming distance of a query (E01-S06-T04). It's a linear scan with
/// <see cref="BitOperations.PopCount(ulong)"/>, which is enough at personal-library scale: 100k hashes take well
/// under a millisecond. Not thread-safe for writes; concurrent searches are fine.
/// </summary>
/// <typeparam name="TKey">Key of each hash, e.g. a media item ID.</typeparam>
public sealed class PerceptualHashIndex<TKey>
    where TKey : notnull
{
    private readonly List<ulong> hashes = [];
    private readonly List<TKey> keys = [];

    /// <summary>
    /// Gets the number of hashes in the index.
    /// </summary>
    public int Count => hashes.Count;

    /// <summary>
    /// Adds a hash. A key can be added more than once, e.g. for the keyframes of a video.
    /// </summary>
    /// <param name="key">Key of the hash.</param>
    /// <param name="hash">The perceptual hash.</param>
    public void Add(TKey key, ulong hash)
    {
        keys.Add(key);
        hashes.Add(hash);
    }

    /// <summary>
    /// Finds the hashes at most <paramref name="maxDistance"/> bits from <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The hash to look for.</param>
    /// <param name="maxDistance">Largest Hamming distance to include, e.g. <see cref="PerceptualHash.SamePictureThreshold"/>.</param>
    /// <returns>The matches, nearest first; ties keep the order they were added in.</returns>
    public IReadOnlyList<PerceptualHashMatch<TKey>> FindWithin(ulong hash, int maxDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);

        var matches = new List<PerceptualHashMatch<TKey>>();
        for (var i = 0; i < hashes.Count; i++)
        {
            var distance = PerceptualHash.Distance(hashes[i], hash);
            if (distance <= maxDistance)
            {
                matches.Add(new PerceptualHashMatch<TKey>(keys[i], distance));
            }
        }

        return [.. matches.OrderBy(match => match.Distance)];
    }
}
