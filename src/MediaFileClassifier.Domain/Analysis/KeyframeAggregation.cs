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
    /// The fewest letters both lines need for <see cref="MergeTextLines"/> to treat them as near-duplicates.
    /// </summary>
    public const int MinFuzzyLetters = 8;

    /// <summary>
    /// The most the letters of two near-duplicate lines may differ, as a share of the longer one's length.
    /// </summary>
    public const double MaxFuzzyDistance = 0.2;

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

        // The tolerance keeps a tag exactly on the threshold, e.g. 3 of 10 frames at 0.3, where 0.3 * 10 is 3.0000000000000004.
        var minFrames = Math.Max(1, (int)Math.Ceiling((minShare * frames.Count) - 1e-9));
        return [.. totals
            .Where(total => total.Value.Frames >= minFrames)
            .Select(total => new AggregatedTag(total.Key, total.Value.Sum / frames.Count, total.Value.Frames))
            .OrderByDescending(tag => tag.Confidence)
            .ThenBy(tag => tag.Name.Value, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Merges the OCR text of each keyframe: lines are trimmed and their whitespace collapsed, empty lines are dropped,
    /// and a line that repeats an earlier one is removed. Repeats are exact, or near: the same text read slightly
    /// differently in another frame (E06-S05-T03). Two lines are near when each has at least
    /// <see cref="MinFuzzyLetters"/> letters, their digits are the same, and their letters, ignoring case and
    /// everything else, differ by at most <see cref="MaxFuzzyDistance"/> of the longer one's length. Of the variants of
    /// a line the one seen most often is kept, the first on a tie. Lines come in the order they were first seen.
    /// </summary>
    /// <param name="frames">Each keyframe's text, or <c>null</c> where none was found.</param>
    /// <returns>The distinct lines.</returns>
    public static IReadOnlyList<string> MergeTextLines(IEnumerable<string?> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var groups = new List<LineGroup>();
        var exact = new Dictionary<string, LineGroup>(StringComparer.Ordinal);
        foreach (var text in frames)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (line.Length == 0)
                {
                    continue;
                }

                if (!exact.TryGetValue(line, out var group))
                {
                    var letters = new string([.. line.Where(char.IsLetter).Select(char.ToLowerInvariant)]);
                    var digits = new string([.. line.Where(char.IsDigit)]);
                    group = letters.Length >= MinFuzzyLetters
                        ? groups.FirstOrDefault(candidate => candidate.IsNear(letters, digits))
                        : null;
                    if (group is null)
                    {
                        group = new LineGroup(letters, digits);
                        groups.Add(group);
                    }

                    exact[line] = group;
                }

                group.Add(line);
            }
        }

        return [.. groups.Select(group => group.Best)];
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

    private static int EditDistance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private sealed class LineGroup(string letters, string digits)
    {
        private readonly Dictionary<string, int> variants = new(StringComparer.Ordinal);
        private readonly List<string> order = [];

        public string Best => order.MaxBy(line => variants[line])!;

        public bool IsNear(string otherLetters, string otherDigits)
        {
            if (letters.Length < MinFuzzyLetters || digits != otherDigits)
            {
                return false;
            }

            var limit = (int)Math.Floor(MaxFuzzyDistance * Math.Max(letters.Length, otherLetters.Length));
            return EditDistance(letters, otherLetters, limit) <= limit;
        }

        public void Add(string line)
        {
            if (variants.TryGetValue(line, out var count))
            {
                variants[line] = count + 1;
                return;
            }

            variants[line] = 1;
            order.Add(line);
        }
    }

    private static double Length(double[] vector) => Math.Sqrt(vector.Sum(value => value * value));
}
