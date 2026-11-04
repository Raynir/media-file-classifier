using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.UseCases.Libraries;

/// <summary>
/// How a folder the user picked overlaps the indexed folders (E02-S03-T02).
/// </summary>
public enum FolderOverlapKind
{
    /// <summary>
    /// No overlap: the folder can be added.
    /// </summary>
    None,

    /// <summary>
    /// The folder is indexed already.
    /// </summary>
    AlreadyIndexed,

    /// <summary>
    /// The folder is inside an indexed folder, which covers it already, so it's refused.
    /// </summary>
    InsideIndexed,

    /// <summary>
    /// The folder contains indexed folders, which are merged into it when it's added.
    /// </summary>
    ContainsIndexed,
}

/// <summary>
/// How a folder overlaps the indexed folders.
/// </summary>
/// <param name="Kind">The kind of overlap.</param>
/// <param name="Folders">The indexed folder that covers it, or the ones it contains.</param>
public sealed record FolderOverlap(FolderOverlapKind Kind, IReadOnlyList<IndexedFolder> Folders)
{
    /// <summary>
    /// Gets the result for no overlap.
    /// </summary>
    public static FolderOverlap None { get; } = new(FolderOverlapKind.None, []);
}
