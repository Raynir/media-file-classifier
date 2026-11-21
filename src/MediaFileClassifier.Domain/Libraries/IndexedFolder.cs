using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// A root folder whose media belongs to a library. Media items store paths relative to it,
/// so the library stays valid when the root moves.
/// </summary>
/// <remarks>
/// <para>
/// The folder is identified by an opaque <see cref="PlatformHandle"/>: a file-system path on Windows,
/// a persisted content URI on Android (E01-S07). Only the platform layer interprets it.
/// <see cref="DisplayPath"/> is for showing to the user.
/// </para>
/// <para>
/// A folder in "selected files only" mode (<see cref="SelectedFilesOnly"/>, D012) is a partial folder: it tracks only
/// the files the user imported from it, which are its items. Discovery and rescans check only those files, and its
/// rules don't apply until <see cref="IndexWholeFolder"/> makes it a full folder (E03-S05).
/// </para>
/// <para>Invariants: the handle and the display path are not blank.</para>
/// </remarks>
public sealed class IndexedFolder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IndexedFolder"/> class from stored values.
    /// </summary>
    /// <param name="id">Folder ID.</param>
    /// <param name="libraryId">Owning library.</param>
    /// <param name="displayPath">Root path as shown to the user.</param>
    /// <param name="platformHandle">Opaque platform handle of the root.</param>
    /// <param name="rules">Include and exclude rules.</param>
    /// <param name="status">Whether the folder is reachable.</param>
    /// <param name="addedAt">When the folder was added.</param>
    /// <param name="statusChangedAt">When <paramref name="status"/> last changed.</param>
    /// <param name="selectedFilesOnly">Whether the folder tracks only the files the user selected.</param>
    /// <param name="volumeLabel">The label of the volume the folder is on, if the platform gave one.</param>
    public IndexedFolder(
        Id<IndexedFolder> id,
        Id<Library> libraryId,
        string displayPath,
        string platformHandle,
        FolderRules rules,
        FolderStatus status,
        DateTimeOffset addedAt,
        DateTimeOffset statusChangedAt,
        bool selectedFilesOnly = false,
        string? volumeLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformHandle);
        ArgumentNullException.ThrowIfNull(rules);

        Id = id;
        LibraryId = libraryId;
        DisplayPath = displayPath;
        PlatformHandle = platformHandle;
        Rules = rules;
        Status = status;
        AddedAt = addedAt;
        StatusChangedAt = statusChangedAt;
        SelectedFilesOnly = selectedFilesOnly;
        VolumeLabel = string.IsNullOrWhiteSpace(volumeLabel) ? null : volumeLabel;
    }

    public Id<IndexedFolder> Id { get; }

    public Id<Library> LibraryId { get; }

    public string DisplayPath { get; private set; }

    public string PlatformHandle { get; private set; }

    public FolderRules Rules { get; private set; }

    public FolderStatus Status { get; private set; }

    public DateTimeOffset AddedAt { get; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is a partial folder, which tracks only the files the user selected (D012).
    /// </summary>
    public bool SelectedFilesOnly { get; private set; }

    /// <summary>
    /// Gets the label of the volume the folder is on, e.g. <c>Backup-2TB</c>, as last read while the folder was Online
    /// (E14-S07-T02), so an Offline folder can say which drive to connect. <see langword="null"/> when the volume has
    /// no label or the platform doesn't give one.
    /// </summary>
    public string? VolumeLabel { get; private set; }

    /// <summary>
    /// Creates a new, online folder with default rules.
    /// </summary>
    /// <param name="libraryId">Owning library.</param>
    /// <param name="displayPath">Root path as shown to the user.</param>
    /// <param name="platformHandle">Opaque platform handle of the root.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new folder.</returns>
    public static IndexedFolder Create(Id<Library> libraryId, string displayPath, string platformHandle, DateTimeOffset now) =>
        new(Id<IndexedFolder>.New(), libraryId, displayPath, platformHandle, new FolderRules(), FolderStatus.Online, now, now);

    /// <summary>
    /// Creates a new, online partial folder, which tracks only the files the user selects in it (D012, E03-S05).
    /// </summary>
    /// <param name="libraryId">Owning library.</param>
    /// <param name="displayPath">Root path as shown to the user.</param>
    /// <param name="platformHandle">Opaque platform handle of the root.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new folder.</returns>
    public static IndexedFolder CreateForSelectedFiles(Id<Library> libraryId, string displayPath, string platformHandle, DateTimeOffset now) =>
        new(Id<IndexedFolder>.New(), libraryId, displayPath, platformHandle, new FolderRules(), FolderStatus.Online, now, now, selectedFilesOnly: true);

    /// <summary>
    /// Makes a partial folder a full folder, so everything in it is indexed with its rules (E03-S05-T03). Its items stay.
    /// </summary>
    public void IndexWholeFolder() => SelectedFilesOnly = false;

    public void ChangeRules(FolderRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Rules = rules;
    }

    /// <summary>
    /// Points the folder at a new root, e.g. after the user moved it. Items keep their relative paths.
    /// </summary>
    /// <param name="displayPath">New root path as shown to the user.</param>
    /// <param name="platformHandle">New opaque platform handle.</param>
    /// <param name="now">Current time.</param>
    public void Relink(string displayPath, string platformHandle, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformHandle);

        DisplayPath = displayPath;
        PlatformHandle = platformHandle;
        SetStatus(FolderStatus.Online, now);
    }

    public void MarkOnline(DateTimeOffset now) => SetStatus(FolderStatus.Online, now);

    /// <summary>
    /// Records the label of the volume the folder is on, read while it's Online.
    /// </summary>
    /// <param name="label">The label, or <see langword="null"/> or blank for none.</param>
    /// <returns><see langword="true"/> when the label changed.</returns>
    public bool ChangeVolumeLabel(string? label)
    {
        label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (label == VolumeLabel)
        {
            return false;
        }

        VolumeLabel = label;
        return true;
    }

    public void MarkOffline(DateTimeOffset now) => SetStatus(FolderStatus.Offline, now);

    private void SetStatus(FolderStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChangedAt = now;
    }
}
