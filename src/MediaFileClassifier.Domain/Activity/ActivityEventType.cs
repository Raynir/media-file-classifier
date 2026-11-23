namespace MediaFileClassifier.Domain.Activity;

/// <summary>
/// What kind of thing happened in the library (E15-S04). The numbers are stored, so never reorder or reuse them.
/// </summary>
public enum ActivityEventType
{
    /// <summary>Files were imported.</summary>
    Import = 1,

    /// <summary>A folder was rescanned.</summary>
    Rescan = 2,

    /// <summary>Items were analysed or analysed again.</summary>
    Analysis = 3,

    /// <summary>Several items were edited at once, or such an edit was undone.</summary>
    BulkEdit = 4,

    /// <summary>Duplicates were resolved (E12-S04-T06).</summary>
    DuplicateResolution = 5,

    /// <summary>A folder was added to the library.</summary>
    FolderAdded = 6,

    /// <summary>A folder was removed from the library.</summary>
    FolderRemoved = 7,

    /// <summary>The library was exported.</summary>
    Export = 8,

    /// <summary>A library was imported.</summary>
    LibraryImport = 9,

    /// <summary>A maintenance job ran, such as regenerating thumbnails.</summary>
    Maintenance = 10,

    /// <summary>Changes found by folder monitoring (MFC-140).</summary>
    FolderMonitoring = 11,

    /// <summary>A folder's drive came back under another drive letter and the folder followed it (E04-S04-T03).</summary>
    FolderRelinked = 12,
}
