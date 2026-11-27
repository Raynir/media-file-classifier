namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// The keeper rules in their default order, and how an order the user saved is made complete (E12-S03-T03).
/// </summary>
public static class KeeperRules
{
    private static readonly string[] FormatOrder =
    [
        // Raw and camera-native formats, then the usual camera output, then lossless exports, then the web formats
        // that conversions produce.
        "dng", "raw", "cr2", "cr3", "nef", "arw", "orf", "rw2", "raf",
        "heic", "heif",
        "jpg", "jpeg",
        "tif", "tiff",
        "png", "bmp", "gif",
        "webp", "avif", "jxl",
    ];

    /// <summary>
    /// Gets the default order: the most user metadata, the highest resolution, the largest file, the original format,
    /// then the oldest.
    /// </summary>
    public static IReadOnlyList<KeeperRule> Default { get; } =
    [
        KeeperRule.MostUserMetadata,
        KeeperRule.HighestResolution,
        KeeperRule.LargestFile,
        KeeperRule.OriginalFormat,
        KeeperRule.Oldest,
    ];

    /// <summary>
    /// Makes a saved order usable: repeated rules and unknown ones are dropped, and rules the order lacks follow in
    /// their default order.
    /// </summary>
    /// <param name="order">The saved order, or <see langword="null"/>.</param>
    /// <returns>An order with every rule exactly once.</returns>
    public static IReadOnlyList<KeeperRule> Normalize(IEnumerable<KeeperRule>? order)
    {
        List<KeeperRule> result = [.. (order ?? []).Where(rule => Enum.IsDefined(rule)).Distinct()];
        result.AddRange(Default.Where(rule => !result.Contains(rule)));
        return result;
    }

    /// <summary>
    /// Gets how original a format is: lower is more original (raw, then HEIC, JPEG, TIFF, lossless exports, and last
    /// the web formats that conversions produce). An unknown format ranks between the lossless exports and the web formats.
    /// </summary>
    /// <param name="format">The format, e.g. <c>.JPG</c>.</param>
    /// <returns>The rank, from 0.</returns>
    public static int FormatRank(string? format)
    {
        var name = (format ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var index = Array.IndexOf(FormatOrder, name);
        return index >= 0 ? index : Array.IndexOf(FormatOrder, "webp") - 1;
    }
}
