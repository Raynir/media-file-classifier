using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// The default <see cref="IVolumeLocator"/> for platforms without drive letters (E04-S04-T03): it knows no volume, so
/// folders are never re-linked to another root or told apart by their volume.
/// </summary>
internal sealed class NoVolumeLocator : IVolumeLocator
{
    /// <inheritdoc />
    public Task<string?> GetVolumeIdAsync(FolderHandle folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<FolderHandle?> FindOnVolumeAsync(FolderHandle folder, string volumeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<FolderHandle?>(null);
}
