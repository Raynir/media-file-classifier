using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// What's known about a file to match against a library: a file on disk with the hashes computed so far, or an item
/// of an imported library package or phone (D004, D076).
/// </summary>
/// <param name="FileName">File name with extension.</param>
/// <param name="MediaType">Kind of media.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="FileModifiedAt">File modification time.</param>
/// <param name="Hashes">Hashes known for the file. Each missing one skips its match tier.</param>
public sealed record FileFacts(string FileName, MediaType MediaType, long SizeBytes, DateTimeOffset FileModifiedAt, FileHashes Hashes);
