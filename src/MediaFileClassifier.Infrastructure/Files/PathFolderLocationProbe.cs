using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Platform;

namespace MediaFileClassifier.Infrastructure.Files;

/// <summary>
/// The portable <see cref="IFolderLocationProbe"/> (E04-S05-T02): a UNC path (<c>\\server\share</c>, or
/// <c>\\?\UNC\server\share</c>) is a network share. It can't see mapped drives or cloud clients, so everything else is
/// local; the Windows head replaces it.
/// </summary>
internal sealed class PathFolderLocationProbe : IFolderLocationProbe
{
    /// <inheritdoc />
    public Task<FolderLocation> ProbeAsync(FolderHandle folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return Task.FromResult(IsUncPath(folder.Id) ? new FolderLocation(IsNetwork: true) : FolderLocation.Local);
    }

    /// <summary>
    /// Tells whether a path is a UNC path. A local device path such as <c>\\?\C:\Photos</c> isn't.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a UNC path.</returns>
    internal static bool IsUncPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return normalized.AsSpan(4).StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase);
        }

        // A server name must follow; "\\" alone isn't a share.
        return normalized.Length > 2 && normalized.StartsWith(@"\\", StringComparison.Ordinal) && normalized[2] != '\\';
    }
}
