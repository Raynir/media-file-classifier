using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// Combines the per-keyframe results of a video or animated image into item-level results (E06-S02-T04, D013).
/// </summary>
public static class KeyframeAggregation
{
    /// <summary>
    /// The default share of keyframes a tag must appear in to be kept.
    /// </summary>
    public const double DefaultMinTagShare = 0.25;

    /// <summary>
    /// Merges the tags of each keyframe. A tag is kept when it appears in at least <paramref name="minShare"/> of the
    /// keyframes, and at least one. Its confidence is the sum of its per-frame confidences divided by the number of
    /// keyframes, so a tag seen in every frame scores higher than one seen in a few. A tag listed twice for one frame
    /// counts once, with its higher confidence.
    /// </summary>
    /// <param name="frames">Each keyframe's tags; a frame with no tags is an empty list.</param>
    /// <param name="minShare">The share of keyframes a tag must appear in, above 0 and at most 1.</param>
    /// <returns>The kept tags, most confident first, then by name.</returns>
    public static IReadOnlyList<AggregatedTag> MergeTags(IReadOnlyList<IReadOnlyList<FrameTag>> frames, double minShare = DefaultMinTagShare)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (double.IsNaN(minShare) || minShare <= 0 || minShare > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minShare), minShare, "The share must be above 0 and at most 1.");
        }

        if (frames.Count == 0)
        {
            return [];
        }

        var totals = new Dictionary<TagName, (double Sum, int Frames)>();
        foreach (var frame in frames)
        {
            foreach (var tag in frame.GroupBy(tag => tag.Name).Select(group => group.MaxBy(tag => tag.Confidence)!))
            {
                var (sum, count) = totals.GetValueOrDefault(tag.Name);
                totals[tag.Name] = (sum + tag.Confidence, count + 1);
            }
        }

        return [.. totals
            .Where(total => total.Value.Frames >= minShare * frames.Count)
            .Select(total => new AggregatedTag(total.Key, total.Value.Sum / frames.Count, total.Value.Frames))
            .OrderByDescending(tag => tag.Confidence)
            .ThenBy(tag => tag.Name.Value, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Merges the OCR text of each keyframe: lines are trimmed and their whitespace collapsed, empty lines are dropped,
    /// and exact repeats are removed, keeping the order in which lines were first seen.
    /// </summary>
    /// <param name="frames">Each keyframe's text, or <c>null</c> where none was found.</param>
    /// <returns>The distinct lines.</returns>
    public static IReadOnlyList<string> MergeTextLines(IEnumerable<string?> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var text in frames)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (line.Length > 0 && seen.Add(line))
                {
                    lines.Add(line);
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// Gets the item embedding: the mean of the L2-normalised keyframe embeddings, normalised again. If the frames
    /// cancel out exactly, which real image embeddings don't, it's the first frame's normalised embedding.
    /// </summary>
    /// <param name="frames">Each keyframe's embedding, all from the same model and of the same length.</param>
    /// <returns>The normalised item embedding.</returns>
    /// <exception cref="ArgumentException">There are no embeddings, their lengths differ, or one is all zeros or not
    /// finite.</exception>
    public static float[] MeanEmbedding(IReadOnlyList<ReadOnlyMemory<float>> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("There are no keyframe embeddings.", nameof(frames));
        }

        var dimension = frames[0].Length;
        var sum = new double[dimension];
        double[]? first = null;
        foreach (var frame in frames)
        {
            if (frame.Length != dimension || dimension == 0)
            {
                throw new ArgumentException("The keyframe embeddings have different or zero lengths.", nameof(frames));
            }

            var normalised = Normalise(frame.Span, nameof(frames));
            first ??= normalised;
            for (var i = 0; i < dimension; i++)
            {
                sum[i] += normalised[i];
            }
        }

        var length = Length(sum);
        var mean = length > 1e-9 ? sum : first!;
        var scale = length > 1e-9 ? length : 1;
        return [.. mean.Select(value => (float)(value / scale))];
    }

    private static double[] Normalise(ReadOnlySpan<float> vector, string parameterName)
    {
        var values = new double[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            values[i] = vector[i];
        }

        var length = Length(values);
        if (!double.IsFinite(length) || length == 0)
        {
            throw new ArgumentException("A keyframe embedding is all zeros or not finite.", parameterName);
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] /= length;
        }

        return values;
    }

    private static double Length(double[] vector) => Math.Sqrt(vector.Sum(value => value * value));
}
