using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Matching;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Matching;

/// <summary>
/// Matches files against one library. The exact tiers query the database by index; the perceptual and file name
/// tiers use the snapshot the matcher was created with.
/// </summary>
internal sealed class FileMatcher : IFileMatcher
{
    private readonly IMediaItemRepository items;
    private readonly PerceptualHashIndex<Id<MediaItem>> perceptualHashes = new();
    private readonly ILookup<string, MediaItemIdentity> byFileName;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileMatcher"/> class.
    /// </summary>
    /// <param name="items">The library's items.</param>
    /// <param name="identities">Snapshot of the items' identity facts.</param>
    public FileMatcher(IMediaItemRepository items, IReadOnlyList<MediaItemIdentity> identities)
    {
        this.items = items;
        foreach (var identity in identities)
        {
            if (identity.PerceptualHash is { } hash && IsVisual(identity.MediaType))
            {
                perceptualHashes.Add(identity.Id, hash);
            }
        }

        byFileName = identities.ToLookup(identity => identity.FileName, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<FileMatch> MatchAsync(FileFacts file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var hashes = file.Hashes;
        if (hashes.ContentHash is { } contentHash)
        {
            var found = await items.FindByContentHashAsync(contentHash, cancellationToken).ConfigureAwait(false);
            if (found.Count > 0)
            {
                return Match(FileMatchType.ContentHash, MatchConfidence.Certain, found.Select(item => new MatchedItem(item.Id)));
            }
        }

        if (hashes.Fingerprint is { } fingerprint)
        {
            var found = await items.FindByFingerprintAsync(fingerprint, file.SizeBytes, cancellationToken).ConfigureAwait(false);

            // Two known content hashes that differ win over a fingerprint collision.
            var consistent = found.Where(item => hashes.ContentHash is null || item.Hashes.ContentHash is null || item.Hashes.ContentHash == hashes.ContentHash).ToList();
            if (consistent.Count > 0)
            {
                return Match(FileMatchType.Fingerprint, MatchConfidence.High, consistent.Select(item => new MatchedItem(item.Id)));
            }
        }

        if (hashes.PerceptualHash is { } perceptualHash && IsVisual(file.MediaType))
        {
            var found = perceptualHashes.FindWithin(perceptualHash, PerceptualHash.SamePictureThreshold);
            if (found.Count > 0)
            {
                return Match(FileMatchType.Perceptual, MatchConfidence.Medium, found.Select(match => new MatchedItem(match.Key, match.Distance)));
            }
        }

        var sameName = byFileName[file.FileName]
            .Where(identity => identity.MediaType == file.MediaType
                && (identity.SizeBytes == file.SizeBytes || SameSecond(identity.FileModifiedAt, file.FileModifiedAt)))
            .ToList();
        return sameName.Count > 0
            ? Match(FileMatchType.FileNameAndMetadata, MatchConfidence.Low, sameName.Select(identity => new MatchedItem(identity.Id)))
            : FileMatch.None;
    }

    private static bool IsVisual(MediaType mediaType) => mediaType is MediaType.Image or MediaType.AnimatedImage;

    private static bool SameSecond(DateTimeOffset first, DateTimeOffset second) => first.ToUnixTimeSeconds() == second.ToUnixTimeSeconds();

    private static FileMatch Match(FileMatchType type, MatchConfidence confidence, IEnumerable<MatchedItem> matched) => new(type, confidence, [.. matched]);
}
