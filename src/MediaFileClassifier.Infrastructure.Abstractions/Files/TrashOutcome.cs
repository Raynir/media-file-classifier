namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// What happened to a file given to <see cref="IFileTrash"/>.
/// </summary>
public enum TrashOutcome
{
    /// <summary>The file is in the trash, and can be restored from there.</summary>
    Moved,

    /// <summary>The file was left where it is, on purpose; <see cref="TrashResult.SkipReason"/> says why.</summary>
    Skipped,

    /// <summary>Moving the file failed; <see cref="TrashResult.Error"/> says why. The file is normally still in place.</summary>
    Failed,
}
