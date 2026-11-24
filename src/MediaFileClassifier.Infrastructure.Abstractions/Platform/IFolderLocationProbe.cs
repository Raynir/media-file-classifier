using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// Tells where a folder is (E04-S05): on a network share, which needs polling because it may not report changes, or in
/// a cloud-synced folder, whose cloud-only files are placeholders (D140). Nothing in the folder is opened or listed.
/// </summary>
public interface IFolderLocationProbe
{
    /// <summary>
    /// Finds where a folder is.
    /// </summary>
    /// <param name="folder">The folder. It needn't be reachable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where it is; <see cref="FolderLocation.Local"/> when the platform can't tell.</returns>
    Task<FolderLocation> ProbeAsync(FolderHandle folder, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a folder is (E04-S05).
/// </summary>
/// <param name="IsNetwork">Whether it's on a network share: a UNC path or a mapped network drive.</param>
/// <param name="CloudProvider">
/// The name of the client that syncs it with the cloud, e.g. <c>OneDrive</c>, or <see langword="null"/> when it isn't in
/// a cloud-synced folder or the platform can't tell.
/// </param>
public sealed record FolderLocation(bool IsNetwork, string? CloudProvider = null)
{
    /// <summary>
    /// Gets a local folder, not synced with the cloud.
    /// </summary>
    public static FolderLocation Local { get; } = new(false);

    /// <summary>
    /// Gets a value indicating whether a cloud client syncs the folder.
    /// </summary>
    public bool IsCloud => CloudProvider is not null;
}
