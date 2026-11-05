using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.UseCases.Import.Stages;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// Pairs new files with items whose file disappeared, so a moved or renamed file keeps its item (E02-S04-T02). A pair
/// needs the same fingerprint and size (D121, High); when the item has a full content hash, the new file's must match
/// too. When several files or items fit, the same file name decides, and without one they stay unpaired.
/// </summary>
/// <param name="hasher">Computes the new files' fingerprints and, to confirm a pair, content hashes.</param>
public sealed class MoveDetector(IMediaHasher hasher)
{
    /// <summary>
    /// Pairs new files with disappeared items. New files whose size some item has get their fingerprint, which stays
    /// on them, so the import doesn't compute it again.
    /// </summary>
    /// <param name="newFiles">The new files with their folders, as unstored items.</param>
    /// <param name="disappeared">Items whose file isn't at its path, from any folder of the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pairs.</returns>
    public async Task<IReadOnlyList<MovedFile>> PairAsync(
        IReadOnlyList<(IndexedFolder Folder, MediaItem File)> newFiles,
        IReadOnlyList<MediaItem> disappeared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newFiles);
        ArgumentNullException.ThrowIfNull(disappeared);

        var bySize = disappeared
            .Where(item => item.Hashes.Fingerprint is not null)
            .ToLookup(item => item.SizeBytes);
        var edges = new List<(IndexedFolder Folder, MediaItem File, MediaItem Item)>();
        foreach (var (folder, file) in newFiles)
        {
            if (!bySize.Contains(file.SizeBytes))
            {
                continue;
            }

            var fingerprint = await hasher.ComputeFingerprintAsync(ItemStage.GetLocation(folder, file), file.SizeBytes, cancellationToken).ConfigureAwait(false);
            file.SetHashes(file.Hashes with { Fingerprint = fingerprint });
            edges.AddRange(bySize[file.SizeBytes].Where(item => item.Hashes.Fingerprint == fingerprint).Select(item => (folder, file, item)));
        }

        var pairs = new List<MovedFile>();
        foreach (var (folder, file, item) in Choose(edges))
        {
            if (await IsSameContentAsync(folder, file, item, cancellationToken).ConfigureAwait(false))
            {
                pairs.Add(new MovedFile(item, folder, file));
            }
        }

        return pairs;
    }

    // One-to-one: a file and an item that only fit each other pair up; among several, only a single same-name pair does.
    private static IEnumerable<(IndexedFolder Folder, MediaItem File, MediaItem Item)> Choose(List<(IndexedFolder Folder, MediaItem File, MediaItem Item)> edges)
    {
        var byFile = edges.ToLookup(edge => edge.File.Id);
        var byItem = edges.ToLookup(edge => edge.Item.Id);
        foreach (var edge in edges)
        {
            var fileEdges = byFile[edge.File.Id].ToList();
            var itemEdges = byItem[edge.Item.Id].ToList();
            if (fileEdges.Count == 1 && itemEdges.Count == 1)
            {
                yield return edge;
            }
            else if (IsSameName(edge)
                && fileEdges.Count(IsSameName) == 1
                && itemEdges.Count(IsSameName) == 1)
            {
                yield return edge;
            }
        }
    }

    private static bool IsSameName((IndexedFolder Folder, MediaItem File, MediaItem Item) edge) =>
        string.Equals(edge.File.FileName, edge.Item.FileName, StringComparison.OrdinalIgnoreCase);

    // A fingerprint samples the file, so a known full hash has the last word (D121).
    private async Task<bool> IsSameContentAsync(IndexedFolder folder, MediaItem file, MediaItem item, CancellationToken cancellationToken)
    {
        if (item.Hashes.ContentHash is null)
        {
            return true;
        }

        var hashes = await hasher.ComputeMissingAsync(ItemStage.GetLocation(folder, file), file.SizeBytes, file.MediaType, file.Hashes, cancellationToken).ConfigureAwait(false);
        file.SetHashes(hashes);
        return hashes.ContentHash == item.Hashes.ContentHash;
    }
}
