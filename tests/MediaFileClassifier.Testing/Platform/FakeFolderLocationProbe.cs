using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Testing.Platform;

/// <summary>
/// An <see cref="IFolderLocationProbe"/> with scripted locations by folder root; any other folder is local. It counts
/// the questions, and can be made to fail.
/// </summary>
public sealed class FakeFolderLocationProbe : IFolderLocationProbe
{
    private int asked;

    /// <summary>
    /// Gets the location of each folder, by its root.
    /// </summary>
    public Dictionary<string, FolderLocation> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the error every question fails with, or <see langword="null"/> to answer.
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// Gets how many times a location was asked for.
    /// </summary>
    public int Asked => Volatile.Read(ref asked);

    /// <summary>
    /// Makes a folder a network share.
    /// </summary>
    /// <param name="root">The folder's root.</param>
    /// <returns>This probe.</returns>
    public FakeFolderLocationProbe Network(string root)
    {
        Locations[root] = new FolderLocation(IsNetwork: true);
        return this;
    }

    /// <inheritdoc />
    public Task<FolderLocation> ProbeAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        Interlocked.Increment(ref asked);
        if (Error is not null)
        {
            return Task.FromException<FolderLocation>(Error);
        }

        lock (Locations)
        {
            return Task.FromResult(Locations.TryGetValue(folder.Id, out var location) ? location : FolderLocation.Local);
        }
    }
}
