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
    public IndexedFolder(
        Id<IndexedFolder> id,
        Id<Library> libraryId,
        string displayPath,
        string platformHandle,
        FolderRules rules,
        FolderStatus status,
        DateTimeOffset addedAt,
        DateTimeOffset statusChangedAt)
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
    /// Creates a new, online folder with default rules.
    /// </summary>
    /// <param name="libraryId">Owning library.</param>
    /// <param name="displayPath">Root path as shown to the user.</param>
    /// <param name="platformHandle">Opaque platform handle of the root.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new folder.</returns>
    public static IndexedFolder Create(Id<Library> libraryId, string displayPath, string platformHandle, DateTimeOffset now) =>
        new(Id<IndexedFolder>.New(), libraryId, displayPath, platformHandle, new FolderRules(), FolderStatus.Online, now, now);

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
