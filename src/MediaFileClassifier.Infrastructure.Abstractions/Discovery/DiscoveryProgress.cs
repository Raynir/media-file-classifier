namespace MediaFileClassifier.Infrastructure.Abstractions.Discovery;

/// <summary>
/// How far a discovery scan got.
/// </summary>
/// <param name="FoldersScanned">Folders listed so far.</param>
/// <param name="FilesFound">Supported media files found so far, new or already in the library.</param>
/// <param name="ItemsRegistered">New items written to the library so far.</param>
public sealed record DiscoveryProgress(int FoldersScanned, int FilesFound, int ItemsRegistered);
