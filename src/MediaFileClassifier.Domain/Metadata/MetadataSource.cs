namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Layer a descriptive value or tag comes from. When several layers have a value, the effective one is
/// User, then AI, then Technical (spec 1.5).
/// </summary>
public enum MetadataSource
{
    /// <summary>
    /// Read from the file, e.g. an embedded title or keywords.
    /// </summary>
    Technical,

    /// <summary>
    /// Produced by an analysis run.
    /// </summary>
    AI,

    /// <summary>
    /// Written or confirmed by the user. See <see cref="UserValueOrigin"/>.
    /// </summary>
    User,
}
