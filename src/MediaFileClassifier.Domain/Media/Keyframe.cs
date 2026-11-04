using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Media;

/// <summary>
/// A frame sampled from a video or animated image for analysis (E06-S02). Per-frame results, such as embeddings,
/// reference it, so search can point to the matching moment.
/// </summary>
/// <remarks>Invariants: the index and the timestamp aren't negative.</remarks>
public sealed record Keyframe
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Keyframe"/> class.
    /// </summary>
    /// <param name="id">Keyframe ID.</param>
    /// <param name="itemId">Item the frame was sampled from.</param>
    /// <param name="index">Position of the keyframe among the item's keyframes, from 0.</param>
    /// <param name="timestamp">Position in the media. For animated images, the sum of the preceding frame delays.</param>
    /// <param name="frameNumber">Frame number in the media, when known.</param>
    /// <param name="perceptualHash">64-bit perceptual hash of the frame, when computed. Re-sampling compares it to skip
    /// unchanged frames, and near-duplicate detection can match videos by their frames.</param>
    public Keyframe(Id<Keyframe> id, Id<MediaItem> itemId, int index, TimeSpan timestamp, int? frameNumber, ulong? perceptualHash = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(timestamp, TimeSpan.Zero);

        Id = id;
        ItemId = itemId;
        Index = index;
        Timestamp = timestamp;
        FrameNumber = frameNumber;
        PerceptualHash = perceptualHash;
    }

    public Id<Keyframe> Id { get; }

    public Id<MediaItem> ItemId { get; }

    public int Index { get; }

    public TimeSpan Timestamp { get; }

    public int? FrameNumber { get; }

    public ulong? PerceptualHash { get; }
}
