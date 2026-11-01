namespace MediaFileClassifier.Domain.Identity;

/// <summary>
/// Globally unique, device-independent ID of a <typeparamref name="TEntity"/> (D066).
/// </summary>
/// <remarks>
/// IDs are UUIDv7: time-sortable, so new rows append to indexes, and unique without coordination,
/// so libraries exported from different devices merge without key collisions (D004).
/// The type parameter keeps IDs of different entities from being mixed up.
/// </remarks>
/// <typeparam name="TEntity">Entity the ID belongs to.</typeparam>
/// <param name="Value">Underlying UUID.</param>
public readonly record struct Id<TEntity>(Guid Value)
{
    /// <summary>
    /// Gets a value indicating whether this is the default, unassigned ID.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>
    /// Creates a new UUIDv7 ID.
    /// </summary>
    /// <returns>The new ID.</returns>
    public static Id<TEntity> New() => new(Guid.CreateVersion7());

    /// <summary>
    /// Parses an ID from its string form, as written by <see cref="ToString"/>.
    /// </summary>
    /// <param name="value">String form of the UUID.</param>
    /// <returns>The parsed ID.</returns>
    public static Id<TEntity> Parse(string value) => new(Guid.Parse(value));

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
