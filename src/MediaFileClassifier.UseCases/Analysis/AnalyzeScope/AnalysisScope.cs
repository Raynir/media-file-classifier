using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.UseCases.Analysis.AnalyzeScope;

/// <summary>
/// Which items a bulk analysis covers (E06-S06-T02, spec 1.9).
/// </summary>
public abstract record AnalysisScope
{
    private protected AnalysisScope()
    {
    }

    /// <summary>
    /// Gets the items that are waiting for analysis: <see cref="MediaItemStatus.Pending"/>, as imported while automatic
    /// analysis was off.
    /// </summary>
    public static AnalysisScope Pending { get; } = new PendingScope();

    /// <summary>
    /// Gets the items of the current search or browse view.
    /// </summary>
    public static AnalysisScope SearchResults { get; } = new SearchResultsScope();

    /// <summary>
    /// Gets the items whose analysis is outdated (E06-S07-T01), for re-indexing (E06-S07-T02).
    /// </summary>
    public static AnalysisScope Outdated { get; } = new OutdatedScope();

    /// <summary>
    /// Gets every item of the library, for re-indexing it all (E06-S07-T02).
    /// </summary>
    public static AnalysisScope Library { get; } = new LibraryScope();

    /// <summary>
    /// Creates a scope of chosen items, such as the selection.
    /// </summary>
    /// <param name="itemIds">The items.</param>
    /// <returns>The scope.</returns>
    public static AnalysisScope Items(IReadOnlyCollection<Id<MediaItem>> itemIds) => new ItemsScope(itemIds);

    /// <summary>
    /// Creates a scope of one indexed folder and everything below it, or only a subfolder's items.
    /// </summary>
    /// <param name="folderId">The indexed folder.</param>
    /// <param name="subfolder">A subfolder path relative to the folder, or <see langword="null"/> for the whole folder.</param>
    /// <returns>The scope.</returns>
    public static AnalysisScope Folder(Id<IndexedFolder> folderId, string? subfolder = null) => new FolderScope(folderId, subfolder);

    /// <summary>
    /// Items chosen by ID.
    /// </summary>
    /// <param name="ItemIds">The items.</param>
    public sealed record ItemsScope(IReadOnlyCollection<Id<MediaItem>> ItemIds) : AnalysisScope;

    /// <summary>
    /// An indexed folder, with its subfolders.
    /// </summary>
    /// <param name="FolderId">The indexed folder.</param>
    /// <param name="Subfolder">A subfolder path relative to the folder, or <see langword="null"/>.</param>
    public sealed record FolderScope(Id<IndexedFolder> FolderId, string? Subfolder) : AnalysisScope;

    /// <summary>
    /// Items waiting for analysis.
    /// </summary>
    public sealed record PendingScope : AnalysisScope;

    /// <summary>
    /// The items of the current search or browse view.
    /// </summary>
    public sealed record SearchResultsScope : AnalysisScope;

    /// <summary>
    /// Items whose analysis is outdated. Each runs only its outdated analyzers (of the chosen ones, if any).
    /// </summary>
    public sealed record OutdatedScope : AnalysisScope;

    /// <summary>
    /// Every item of the library.
    /// </summary>
    public sealed record LibraryScope : AnalysisScope;
}
