using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Repositories;

/// <summary>
/// Stores how each analyzer last ended for each item (E06-S01-T03), and applies an analyzer's run together with its
/// state.
/// </summary>
public interface IAnalyzerStateRepository
{
    /// <summary>
    /// Gets an item's analyzer states.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One state per analyzer that has run, in no particular order.</returns>
    Task<IReadOnlyList<AnalyzerState>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes analyzer states, replacing each item's earlier state of the same analyzer, in one write.
    /// </summary>
    /// <param name="states">The states.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the states are stored.</returns>
    /// <exception cref="KeyNotFoundException">An item doesn't exist.</exception>
    Task SetAsync(IReadOnlyCollection<AnalyzerState> states, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the items whose analyzer ended with a status, e.g. every item whose OCR failed, for a retry.
    /// </summary>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="status">The status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items' IDs.</returns>
    Task<IReadOnlyList<Id<MediaItem>>> GetItemsAsync(string analyzer, AnalyzerStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an analyzer's completed run to the item's metadata (<see cref="ItemMetadata.ApplyRun"/>) and writes the
    /// analyzer's state, in one write, so the result and its state are stored together or not at all.
    /// </summary>
    /// <param name="run">The completed run.</param>
    /// <param name="values">Field values the run produced.</param>
    /// <param name="tags">AI tag assignments the run produced.</param>
    /// <param name="retainedPreviousRuns">How many previous runs of the analyzer to keep.</param>
    /// <param name="state">The analyzer's new state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pruned runs, and whether the effective values changed.</returns>
    /// <exception cref="KeyNotFoundException">The item doesn't exist.</exception>
    Task<RunApplication> ApplyRunAsync(
        AnalysisRun run,
        IReadOnlyCollection<AiFieldValue> values,
        IReadOnlyCollection<TagAssignment> tags,
        int retainedPreviousRuns,
        AnalyzerState state,
        CancellationToken cancellationToken = default);
}
