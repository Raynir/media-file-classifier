using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediatR;

namespace MediaFileClassifier.UseCases.Import.ImportFiles;

/// <summary>
/// Imports files the user selected, by picking them (E03-S05) or dropping them (E03-S06). Each file goes into the
/// indexed folder that covers it, or into a new partial folder for its parent folder (D012), and new files are imported
/// in the background (E03-S07).
/// </summary>
/// <param name="Files">The files, each with the folder it's in.</param>
public sealed record ImportFilesCommand(IReadOnlyList<FileLocation> Files) : IRequest<ImportFilesResult>;

/// <summary>
/// What importing selected files did. Each file is in one list.
/// </summary>
public sealed record ImportFilesResult
{
    /// <summary>
    /// Gets the files being imported: new ones, files the user had removed and are restored (E02-S10-T04), and files a
    /// running import of their folder picks up.
    /// </summary>
    public IReadOnlyList<FileLocation> Imported { get; init; } = [];

    /// <summary>
    /// Gets the files the library has already.
    /// </summary>
    public IReadOnlyList<FileLocation> AlreadyInLibrary { get; init; } = [];

    /// <summary>
    /// Gets the files the rules of the full folder they're in leave out.
    /// </summary>
    public IReadOnlyList<FileLocation> ExcludedByRules { get; init; } = [];

    /// <summary>
    /// Gets the files that aren't a supported format.
    /// </summary>
    public IReadOnlyList<FileLocation> Unsupported { get; init; } = [];

    /// <summary>
    /// Gets the files that couldn't be read, or aren't there any more.
    /// </summary>
    public IReadOnlyList<FileLocation> Unreadable { get; init; } = [];

    /// <summary>
    /// Gets the partial folders added for files outside the indexed folders.
    /// </summary>
    public IReadOnlyList<IndexedFolder> AddedFolders { get; init; } = [];
}
