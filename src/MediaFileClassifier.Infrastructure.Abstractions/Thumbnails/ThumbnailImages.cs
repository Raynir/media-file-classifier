using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;

/// <summary>
/// The encoded thumbnails of one item, or none and the reason as a warning.
/// </summary>
public sealed record ThumbnailImages
{
    /// <summary>
    /// Gets the encoded image of each size, empty when no thumbnail could be made.
    /// </summary>
    public IReadOnlyDictionary<ThumbnailSize, byte[]> Images { get; init; } = new Dictionary<ThumbnailSize, byte[]>();

    /// <summary>
    /// Gets the problems met, such as <see cref="ThumbnailWarnings.ThumbnailFailed"/>.
    /// </summary>
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether thumbnails were made. Without them the item shows a placeholder.
    /// </summary>
    public bool HasImages => Images.Count > 0;

    /// <summary>
    /// Creates a result without thumbnails.
    /// </summary>
    /// <param name="warning">Why there are none.</param>
    /// <returns>The result.</returns>
    public static ThumbnailImages Failed(ExtractionWarning warning) => new() { Warnings = [warning] };
}
