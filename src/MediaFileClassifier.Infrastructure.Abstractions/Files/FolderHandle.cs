namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// A folder the user granted the app access to. Code never assumes the folder has a file-system path:
/// on desktop <see cref="Id"/> is an absolute path, on Android it will be a persisted content URI (E20).
/// </summary>
/// <param name="Id">Platform-specific identifier of the folder. Store it as is, and don't parse it.</param>
/// <param name="DisplayName">Name to show the user.</param>
public sealed record FolderHandle(string Id, string DisplayName);
