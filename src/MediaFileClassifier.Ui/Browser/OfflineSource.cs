using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// Names what to connect for an Offline folder (E14-S07-T02): the drive by its label, e.g. <c>drive 'Backup-2TB'</c>;
/// an unlabelled drive by its letter (<c>drive E:</c>); a network share by its share (<c>share \\nas\photos</c>);
/// otherwise only the folder, e.g. on Android, where folders aren't paths.
/// </summary>
public static class OfflineSource
{
    /// <summary>
    /// Describes the drive or share an Offline folder is on.
    /// </summary>
    /// <param name="folderPath">The folder's path as shown to the user.</param>
    /// <param name="volumeLabel">The drive's label, or <see langword="null"/>.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The text, or <see langword="null"/> when only the folder can be named.</returns>
    public static string? Describe(string? folderPath, string? volumeLabel, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        if (!string.IsNullOrWhiteSpace(volumeLabel))
        {
            return localizer.Format("Offline_Drive_Label", volumeLabel);
        }

        if (string.IsNullOrEmpty(folderPath))
        {
            return null;
        }

        if (folderPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share\rest: the share is the first two parts.
            var parts = folderPath[2..].Split('\\', 3);
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0
                ? localizer.Format("Offline_Share", $@"\\{parts[0]}\{parts[1]}")
                : null;
        }

        // Checked by hand rather than with Path, which knows only the running platform's paths.
        return folderPath.Length >= 3 && char.IsAsciiLetter(folderPath[0]) && folderPath[1] == ':' && folderPath[2] is '\\' or '/'
            ? localizer.Format("Offline_Drive_Letter", folderPath[..2].ToUpperInvariant())
            : null;
    }

    /// <summary>
    /// Gets the text after "Offline: " in an Offline item's badge and details: the drive or share and the folder.
    /// </summary>
    /// <param name="folderPath">The folder's path as shown to the user.</param>
    /// <param name="volumeLabel">The drive's label, or <see langword="null"/>.</param>
    /// <param name="localizer">UI strings.</param>
    /// <returns>The text, e.g. <c>drive 'Backup-2TB' (E:\Photos)</c>.</returns>
    public static string Detail(string folderPath, string? volumeLabel, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        return Describe(folderPath, volumeLabel, localizer) is { } source
            ? localizer.Format("Offline_SourceAndFolder", source, folderPath)
            : localizer.Format("Tile_Badge_OfflineFolder", folderPath);
    }
}
