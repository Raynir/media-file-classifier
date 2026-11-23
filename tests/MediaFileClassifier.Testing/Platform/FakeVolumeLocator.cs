using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Testing.Platform;

/// <summary>
/// An <see cref="IVolumeLocator"/> with scripted volumes: the identity of the volume under each drive root, and where
/// a folder is found when its volume moved to another root.
/// </summary>
public sealed class FakeVolumeLocator : IVolumeLocator
{
    /// <summary>
    /// Gets the identity of the volume under each drive root, e.g. <c>E:\</c>.
    /// </summary>
    public Dictionary<string, string> Volumes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets where a folder, by its stored root, is found on its volume under another root.
    /// </summary>
    public Dictionary<string, FolderHandle> Moved { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets how many times a folder was looked for under another root.
    /// </summary>
    public int Searches { get; private set; }

    /// <inheritdoc />
    public Task<string?> GetVolumeIdAsync(FolderHandle folder, CancellationToken cancellationToken = default) =>
        Task.FromResult(folder.Id.Length >= 3 ? Volumes.GetValueOrDefault(folder.Id[..3]) : null);

    /// <inheritdoc />
    public Task<FolderHandle?> FindOnVolumeAsync(FolderHandle folder, string volumeId, CancellationToken cancellationToken = default)
    {
        Searches++;
        return Task.FromResult(Moved.GetValueOrDefault(folder.Id));
    }
}
