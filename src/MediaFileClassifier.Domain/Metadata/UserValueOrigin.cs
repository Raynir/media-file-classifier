namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// How a user-layer value was created (D020). Both kinds are user-owned: re-analysis never replaces them.
/// </summary>
public enum UserValueOrigin
{
    /// <summary>
    /// The user changed or wrote the value.
    /// </summary>
    Edited,

    /// <summary>
    /// The user confirmed an AI value without changing it. The value keeps a reference to the run it came from.
    /// </summary>
    Accepted,
}
