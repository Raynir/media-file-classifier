using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Analysis;

/// <summary>
/// The items whose results came from another provider, model or prompt version than the current one (E06-S07-T01),
/// per analyzer.
/// </summary>
public sealed class OutdatedAnalysis
{
    private readonly Dictionary<string, IReadOnlyList<Id<MediaItem>>> byAnalyzer;
    private readonly Dictionary<string, HashSet<Id<MediaItem>>> lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutdatedAnalysis"/> class.
    /// </summary>
    /// <param name="byAnalyzer">The outdated items of each analyzer; analyzers without any may be left out.</param>
    public OutdatedAnalysis(IReadOnlyDictionary<string, IReadOnlyList<Id<MediaItem>>> byAnalyzer)
    {
        ArgumentNullException.ThrowIfNull(byAnalyzer);

        this.byAnalyzer = byAnalyzer
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Id<MediaItem>>)[.. pair.Value.Distinct()], StringComparer.Ordinal);
        lookup = this.byAnalyzer.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet(), StringComparer.Ordinal);
        ItemIds = [.. this.byAnalyzer.Values.SelectMany(ids => ids).Distinct()];
    }

    /// <summary>
    /// Gets an empty result: nothing is outdated.
    /// </summary>
    public static OutdatedAnalysis None { get; } = new(new Dictionary<string, IReadOnlyList<Id<MediaItem>>>());

    /// <summary>
    /// Gets the outdated items, each once, in no particular order.
    /// </summary>
    public IReadOnlyList<Id<MediaItem>> ItemIds { get; }

    /// <summary>
    /// Gets how many items are outdated.
    /// </summary>
    public int Count => ItemIds.Count;

    /// <summary>
    /// Gets the analyzers that have outdated items.
    /// </summary>
    public IReadOnlyCollection<string> Analyzers => byAnalyzer.Keys;

    /// <summary>
    /// Gets the outdated items of one analyzer.
    /// </summary>
    /// <param name="analyzer">Analyzer key.</param>
    /// <returns>The items, or none.</returns>
    public IReadOnlyList<Id<MediaItem>> For(string analyzer) => byAnalyzer.TryGetValue(analyzer, out var ids) ? ids : [];

    /// <summary>
    /// Gets the analyzers whose results of an item are outdated, in the order given.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="order">Analyzer keys in running order, e.g. the profile's.</param>
    /// <returns>The analyzers.</returns>
    public IReadOnlyList<string> AnalyzersOf(Id<MediaItem> itemId, IEnumerable<string> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return [.. order.Where(analyzer => lookup.TryGetValue(analyzer, out var ids) && ids.Contains(itemId))];
    }
}
