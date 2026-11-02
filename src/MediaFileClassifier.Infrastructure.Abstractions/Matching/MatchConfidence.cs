namespace MediaFileClassifier.Infrastructure.Abstractions.Matching;

/// <summary>
/// How sure a file match is. A library package merge applies <see cref="Certain"/> and <see cref="High"/> matches
/// automatically, and asks the user to confirm <see cref="Medium"/> and <see cref="Low"/> ones (D076).
/// </summary>
public enum MatchConfidence
{
    /// <summary>
    /// No match.
    /// </summary>
    None,

    /// <summary>
    /// A file name and metadata match.
    /// </summary>
    Low,

    /// <summary>
    /// A perceptual match.
    /// </summary>
    Medium,

    /// <summary>
    /// A fingerprint and size match.
    /// </summary>
    High,

    /// <summary>
    /// A content hash match.
    /// </summary>
    Certain,
}
