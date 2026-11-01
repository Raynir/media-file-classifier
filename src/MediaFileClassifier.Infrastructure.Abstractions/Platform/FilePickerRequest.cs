namespace MediaFileClassifier.Infrastructure.Abstractions.Platform;

/// <summary>
/// What the file picker asks for.
/// </summary>
/// <param name="Title">Title of the picker dialog.</param>
/// <param name="AllowMultiple">Whether the user can pick more than one file.</param>
/// <param name="FileTypes">File types to offer. Empty means all files.</param>
public sealed record FilePickerRequest(string Title, bool AllowMultiple, IReadOnlyList<FileTypeFilter> FileTypes);
