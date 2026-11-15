using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// The items an <see cref="AnalysisScope"/> resolved to (E06-S06-T02), with the counts the time estimate needs.
/// Missing items are left out, since there is no file to analyze.
/// </summary>
/// <param name="ItemIds">The items.</param>
/// <param name="Images">How many are images or animated images.</param>
/// <param name="Videos">How many are videos.</param>
/// <param name="VideoFrames">How many frames the videos get sampled into in all, the work a video costs.</param>
public sealed record AnalysisScopeItems(IReadOnlyList<Id<MediaItem>> ItemIds, int Images, int Videos, int VideoFrames = 0)
{
    /// <summary>
    /// Gets the analyzers each item runs, when they differ per item (the outdated scope); <see langword="null"/> when
    /// every item runs the same ones.
    /// </summary>
    public IReadOnlyDictionary<Id<MediaItem>, IReadOnlyList<string>>? ItemAnalyzers { get; init; }

    /// <summary>
    /// Gets a scope with no items.
    /// </summary>
    public static AnalysisScopeItems Empty { get; } = new([], 0, 0);

    /// <summary>
    /// Gets how many items there are.
    /// </summary>
    public int Count => ItemIds.Count;
}
