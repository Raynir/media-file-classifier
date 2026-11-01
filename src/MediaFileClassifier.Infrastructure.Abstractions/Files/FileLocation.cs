namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// A file or subfolder inside a folder the user granted access to.
/// </summary>
/// <param name="Folder">The granted folder.</param>
/// <param name="RelativePath">Path inside the folder with <c>/</c> separators, or an empty string for the folder itself.</param>
public sealed record FileLocation(FolderHandle Folder, string RelativePath);
