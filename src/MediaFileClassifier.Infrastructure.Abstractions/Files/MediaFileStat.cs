namespace MediaFileClassifier.Infrastructure.Abstractions.Files;

/// <summary>
/// Size and modification time of a file in a granted folder.
/// </summary>
/// <param name="RelativePath">Path inside the folder with <c>/</c> separators.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="LastModifiedUtc">Last write time, in UTC.</param>
public sealed record MediaFileStat(string RelativePath, long Length, DateTimeOffset LastModifiedUtc)
{
    /// <summary>
    /// Gets the creation time, in UTC, or <see langword="null"/> when the platform doesn't report it.
    /// </summary>
    public DateTimeOffset? CreatedUtc { get; init; }

    /// <summary>
    /// Gets a value indicating whether the file is a cloud placeholder, such as a OneDrive Files On-Demand file that
    /// is only online. Reading it would download it, so discovery skips it unless the folder allows it.
    /// </summary>
    public bool IsCloudPlaceholder { get; init; }
}
