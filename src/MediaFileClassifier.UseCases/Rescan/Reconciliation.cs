using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// How the files on disk compare with the library's items after a rescan's listing (E02-S04-T01). Nothing is written
/// yet: <see cref="ReconcileStage"/> applies it. Items the rules or the user excluded aren't part of it.
/// </summary>
public sealed record Reconciliation
{
    /// <summary>
    /// Gets the folders that were listed, with what their scan found.
    /// </summary>
    public IReadOnlyList<(IndexedFolder Folder, DiscoveryResult Result)> Scanned { get; init; } = [];

    /// <summary>
    /// Gets the folders that couldn't be listed (D188). Their items are left as they are.
    /// </summary>
    public IReadOnlyList<IndexedFolder> Offline { get; init; } = [];

    /// <summary>
    /// Gets the supported media files no item matches, as Discovered items that aren't stored yet.
    /// </summary>
    public IReadOnlyList<MediaItem> New { get; init; } = [];

    /// <summary>
    /// Gets the items whose file is at its path with the stored size and modification time, and the items under a
    /// subfolder or link the scan couldn't follow, which can't be checked.
    /// </summary>
    public IReadOnlyList<MediaItem> Unchanged { get; init; } = [];

    /// <summary>
    /// Gets the items whose file is at its path with another size or modification time, and Missing items whose file is
    /// back at its path. Whether the content changed is decided by fingerprinting (E02-S04-T03).
    /// </summary>
    public IReadOnlyList<ModifiedFile> Modified { get; init; } = [];

    /// <summary>
    /// Gets the items whose file was moved or renamed within the library (E02-S04-T02).
    /// </summary>
    public IReadOnlyList<MovedFile> Moved { get; init; } = [];

    /// <summary>
    /// Gets the items of the listed folders whose file is gone, including those already Missing.
    /// </summary>
    public IReadOnlyList<MediaItem> Missing { get; init; } = [];
}

/// <summary>
/// An item whose file changed on disk, or came back.
/// </summary>
/// <param name="Item">The item, as stored.</param>
/// <param name="File">The file as listed now.</param>
public sealed record ModifiedFile(MediaItem Item, MediaFileStat File);

/// <summary>
/// An item whose file is now somewhere else in the library.
/// </summary>
/// <param name="Item">The item, as stored.</param>
/// <param name="Folder">The folder the file is in now.</param>
/// <param name="Found">The file at its new place, as an unstored Discovered item with its fingerprint.</param>
public sealed record MovedFile(MediaItem Item, IndexedFolder Folder, MediaItem Found);
