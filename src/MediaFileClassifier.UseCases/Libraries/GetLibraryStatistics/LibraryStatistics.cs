namespace MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;

/// <summary>
/// The item counts of the open library (E02-S05-T01). Excluded items don't count anywhere (MFC-123).
/// </summary>
/// <param name="Images">Still images.</param>
/// <param name="AnimatedImages">Animated images.</param>
/// <param name="Videos">Videos.</param>
/// <param name="Indexed">Items with status Indexed.</param>
/// <param name="Pending">Items still to do: status Discovered or Pending.</param>
/// <param name="Errors">Items with status Error.</param>
/// <param name="Missing">Items whose file is missing.</param>
/// <param name="MediaSizeBytes">The summed file size of the items whose file is there, i.e. all but Missing.</param>
public sealed record LibraryStatistics(
    int Images,
    int AnimatedImages,
    int Videos,
    int Indexed,
    int Pending,
    int Errors,
    int Missing,
    long MediaSizeBytes)
{
    /// <summary>
    /// Gets the statistics of an empty library.
    /// </summary>
    public static LibraryStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Gets the number of items.
    /// </summary>
    public int Total => Images + AnimatedImages + Videos;
}
