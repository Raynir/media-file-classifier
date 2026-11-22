using MediaFileClassifier.Infrastructure.Abstractions.Files;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// A change of a watched folder, after debouncing and stability detection (E04-S01-T02). Paths are relative to the
/// folder, with <c>/</c> separators.
/// </summary>
/// <param name="Kind">What changed.</param>
/// <param name="RelativePath">The file's path; for a rename, its new path.</param>
/// <param name="OldRelativePath">For a rename, the old path; otherwise <see langword="null"/>.</param>
/// <param name="Stat">The file's size and times when it was found stable; <see langword="null"/> for a deletion.</param>
public sealed record FolderChange(FolderChangeKind Kind, string RelativePath, string? OldRelativePath = null, MediaFileStat? Stat = null);
