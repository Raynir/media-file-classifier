namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// Size and modification time of a file in a granted folder.
/// </summary>
/// <param name="RelativePath">Path inside the folder with <c>/</c> separators.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="LastModifiedUtc">Last write time, in UTC.</param>
public sealed record MediaFileStat(string RelativePath, long Length, DateTimeOffset LastModifiedUtc);
