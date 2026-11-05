using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// What a folder holds now, for a rescan to compare with the library (E02-S04-T01). Nothing is written to the library.
/// </summary>
public sealed record FolderSnapshot
{
    /// <summary>
    /// Gets the counts and the folders and links the scan couldn't follow. <see cref="DiscoveryResult.ItemsRegistered"/>
    /// is 0, because nothing is registered.
    /// </summary>
    public required DiscoveryResult Result { get; init; }

    /// <summary>
    /// Gets the files at paths the folder already has items for, as listed. They aren't opened.
    /// </summary>
    public IReadOnlyList<MediaFileStat> KnownFiles { get; init; } = [];

    /// <summary>
    /// Gets the supported media files at paths the folder has no item for, as Discovered items that aren't stored yet.
    /// </summary>
    public IReadOnlyList<MediaItem> NewFiles { get; init; } = [];
}
