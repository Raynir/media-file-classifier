using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.UseCases.Duplicates.Visual;

/// <summary>
/// Decides whether two items are visual near-duplicates (E12-S02-T01). A match is one of:
/// <list type="bullet">
/// <item>perceptual hashes at most <see cref="HashDistance"/> apart, confirmed by an embedding similarity of at least
/// <see cref="ConfirmSimilarity"/>, or by the hashes alone when either item has no embedding yet;</item>
/// <item>hashes at most <see cref="WideHashDistance"/> apart with a similarity of at least <see cref="WideSimilarity"/>;</item>
/// <item>a similarity of at least <see cref="EmbeddingSimilarity"/> alone.</item>
/// </list>
/// Videos and animated images compare their keyframes' hashes and must have about the same duration
/// (E12-S02-T02); stills never match them. The rules are symmetric, so groups don't depend on the order items are
/// checked in. The thresholds were measured with SigLIP 2 on the evaluation corpus (see <c>docs/duplicates.md</c>).
/// </summary>
/// <param name="HashDistance">Largest hash distance of a close match.</param>
/// <param name="ConfirmSimilarity">Smallest similarity that confirms a close match.</param>
/// <param name="WideHashDistance">Largest hash distance of a wide match; negative turns wide matches off.</param>
/// <param name="WideSimilarity">Smallest similarity that confirms a wide match.</param>
/// <param name="EmbeddingSimilarity">Smallest similarity that is a match on its own; above 1 turns it off.</param>
public sealed record VisualMatchRules(int HashDistance, float ConfirmSimilarity, int WideHashDistance, float WideSimilarity, float EmbeddingSimilarity)
{
    /// <summary>
    /// Shortest duration difference always accepted between two videos or animations.
    /// </summary>
    public static readonly TimeSpan DurationSlack = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Share of the longer duration two durations may differ by.
    /// </summary>
    public const double DurationShare = 0.02;

    /// <summary>
    /// Share of one item's keyframes that must have a close keyframe in the other.
    /// </summary>
    public const double FrameShare = 0.5;

    /// <summary>
    /// Gets the rules of the strict sensitivity: copies saved differently (re-encoded, resized, converted, thumbnails)
    /// and light tonal edits, which neither signal tells apart.
    /// </summary>
    public static VisualMatchRules Strict { get; } = new(4, 0.80f, -1, 2, 2);

    /// <summary>
    /// Gets the rules of the loose sensitivity: also crops, edits and burst shots.
    /// </summary>
    public static VisualMatchRules Loose { get; } = new(4, 0.80f, 16, 0.90f, 0.95f);

    /// <summary>
    /// Gets the largest hash distance any rule accepts, for finding candidates.
    /// </summary>
    public int CandidateHashDistance => Math.Max(HashDistance, WideHashDistance);

    /// <summary>
    /// Gets a value indicating whether embedding similarity alone finds candidates.
    /// </summary>
    public bool UsesEmbeddingCandidates => EmbeddingSimilarity <= 1;

    /// <summary>
    /// Gets a value indicating whether two items are of kinds that can match: two stills, or two of videos and
    /// animated images.
    /// </summary>
    /// <param name="first">One item.</param>
    /// <param name="second">The other item.</param>
    /// <returns>Whether they can match.</returns>
    public static bool Comparable(VisualCandidate first, VisualCandidate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return (first.MediaType == MediaType.Image) == (second.MediaType == MediaType.Image);
    }

    /// <summary>
    /// Decides whether two items match.
    /// </summary>
    /// <param name="first">One item.</param>
    /// <param name="second">The other item.</param>
    /// <param name="similarity">Their embedding similarity, or <see cref="float.NaN"/> when either has no embedding.</param>
    /// <returns>Whether they're visual near-duplicates.</returns>
    public bool Matches(VisualCandidate first, VisualCandidate second, float similarity)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (!Comparable(first, second) || !SimilarDuration(first, second))
        {
            return false;
        }

        if (similarity >= EmbeddingSimilarity)
        {
            return true;
        }

        if ((float.IsNaN(similarity) || similarity >= ConfirmSimilarity) && HashesMatch(first, second, HashDistance))
        {
            return true;
        }

        return WideHashDistance >= 0 && similarity >= WideSimilarity && HashesMatch(first, second, WideHashDistance);
    }

    /// <summary>
    /// Gets whether the perceptual hashes are within a distance: a still's hash, or enough keyframes.
    /// </summary>
    /// <param name="first">One item.</param>
    /// <param name="second">The other item.</param>
    /// <param name="distance">Largest distance.</param>
    /// <returns>Whether the hashes match.</returns>
    public static bool HashesMatch(VisualCandidate first, VisualCandidate second, int distance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        // An item without keyframes counts its one perceptual hash as its only frame, so an animation that has only
        // its first frame's hash can still match a video of it. The fewer hashes are looked up in the more, so the
        // rule is symmetric; two stills simply compare their hashes.
        var (firstHashes, secondHashes) = (HashesOf(first), HashesOf(second));
        if (firstHashes.Count == 0 || secondHashes.Count == 0)
        {
            return false;
        }

        var (few, many) = firstHashes.Count <= secondHashes.Count ? (firstHashes, secondHashes) : (secondHashes, firstHashes);
        var close = few.Count(frame => many.Any(other => PerceptualHash.Distance(frame, other) <= distance));
        return close >= few.Count * FrameShare;
    }

    private static IReadOnlyList<ulong> HashesOf(VisualCandidate candidate) =>
        candidate.FrameHashes.Count > 0 ? candidate.FrameHashes : candidate.PerceptualHash is { } hash ? [hash] : [];

    private static bool SimilarDuration(VisualCandidate first, VisualCandidate second)
    {
        if (first.Duration is not { } a || second.Duration is not { } b)
        {
            return true;
        }

        var longer = a > b ? a : b;
        var slack = Math.Max(DurationSlack.TotalMilliseconds, longer.TotalMilliseconds * DurationShare);
        return Math.Abs((a - b).TotalMilliseconds) <= slack;
    }
}
