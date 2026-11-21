namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// What registering a selected file did (E03-S05).
/// </summary>
public enum FileRegistrationOutcome
{
    /// <summary>The file was registered as a new Discovered item.</summary>
    Registered,

    /// <summary>The folder already has an item at the file's path.</summary>
    AlreadyInLibrary,

    /// <summary>A full folder's rules leave the file out: an excluded pattern or subfolder, a hidden file or a cloud placeholder.</summary>
    ExcludedByRules,

    /// <summary>The file's extension or content isn't a supported format.</summary>
    Unsupported,

    /// <summary>The file couldn't be read, or isn't there any more.</summary>
    Unreadable,
}
