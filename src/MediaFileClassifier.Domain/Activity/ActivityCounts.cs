namespace MediaFileClassifier.Domain.Activity;

/// <summary>
/// The names of the counts an <see cref="ActivityEvent"/> can carry. The event stores plain numbers under these
/// names, and the view turns them into text in the user's language, so what is stored doesn't depend on the language.
/// </summary>
public static class ActivityCounts
{
    /// <summary>Items the activity handled in all.</summary>
    public const string Items = "items";

    /// <summary>Items added to the library.</summary>
    public const string Added = "added";

    /// <summary>Items that failed.</summary>
    public const string Failed = "failed";

    /// <summary>Items whose file changed.</summary>
    public const string Modified = "modified";

    /// <summary>Items whose file moved.</summary>
    public const string Moved = "moved";

    /// <summary>Items whose file is gone.</summary>
    public const string Missing = "missing";

    /// <summary>Folders the activity covered.</summary>
    public const string Folders = "folders";

    /// <summary>Folders that were offline.</summary>
    public const string Offline = "offline";

    /// <summary>Items the user edited or resolved.</summary>
    public const string Edited = "edited";

    /// <summary>Items whose last edit was put back.</summary>
    public const string Restored = "restored";
}
