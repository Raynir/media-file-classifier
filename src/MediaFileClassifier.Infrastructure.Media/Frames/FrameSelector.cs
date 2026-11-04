using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;

namespace MediaFileClassifier.Infrastructure.Media.Frames;

/// <summary>
/// Decides which frames, offered in time order, are kept (E06-S02-T02). A blank frame is dropped when
/// <see cref="FrameSamplingOptions.DropBlankFrames"/> is on, and a frame within
/// <see cref="FrameSamplingOptions.DuplicateThreshold"/> bits of the previous kept frame is dropped as a near
/// duplicate. If every frame is blank, the brightest is kept, so the item is still analysed.
/// </summary>
/// <typeparam name="T">The frame.</typeparam>
/// <param name="options">The options.</param>
internal sealed class FrameSelector<T>(FrameSamplingOptions options)
{
    private readonly List<T> kept = [];
    private ulong? lastKeptHash;
    private (T Frame, double MeanLuma)? brightestBlank;

    /// <summary>
    /// Gets the kept frames, in the order offered.
    /// </summary>
    public IReadOnlyList<T> Kept => kept.Count > 0 || brightestBlank is not { } blank ? kept : [blank.Frame];

    /// <summary>
    /// Gets how many frames were offered.
    /// </summary>
    public int Offered { get; private set; }

    /// <summary>
    /// Offers the next frame.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="hash">Its perceptual hash.</param>
    /// <param name="isBlank">Whether it's nearly black or one flat colour.</param>
    /// <param name="meanLuma">Its mean luma, 0–255.</param>
    /// <returns><c>true</c> if the frame is kept so far; a blank frame may still be kept at the end.</returns>
    public bool Offer(T frame, ulong hash, bool isBlank, double meanLuma)
    {
        Offered++;
        if (isBlank && options.DropBlankFrames)
        {
            if (brightestBlank is not { } blank || meanLuma > blank.MeanLuma)
            {
                brightestBlank = (frame, meanLuma);
            }

            return false;
        }

        if (lastKeptHash is { } last && PerceptualHash.Distance(last, hash) <= options.DuplicateThreshold)
        {
            return false;
        }

        kept.Add(frame);
        lastKeptHash = hash;
        return true;
    }
}
