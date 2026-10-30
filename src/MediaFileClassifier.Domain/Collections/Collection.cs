using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Collections;

/// <summary>
/// A user-made group of items (E11). Collections start flat; <see cref="ParentId"/> is reserved for nesting,
/// so adding it later needs no data migration (D023).
/// </summary>
/// <remarks>Invariants: the name is not blank, and a collection isn't its own parent.</remarks>
public sealed class Collection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Collection"/> class from stored values.
    /// </summary>
    /// <param name="id">Collection ID.</param>
    /// <param name="name">Display name.</param>
    /// <param name="parentId">Parent collection. Unused until nesting is built.</param>
    /// <param name="createdAt">Creation date.</param>
    public Collection(Id<Collection> id, string name, Id<Collection>? parentId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (parentId == id)
        {
            throw new ArgumentException("A collection can't be its own parent.", nameof(parentId));
        }

        Id = id;
        Name = name.Trim();
        ParentId = parentId;
        CreatedAt = createdAt;
    }

    public Id<Collection> Id { get; }

    public string Name { get; private set; }

    public Id<Collection>? ParentId { get; }

    public DateTimeOffset CreatedAt { get; }

    public static Collection Create(string name, DateTimeOffset now) => new(Id<Collection>.New(), name, null, now);

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }
}
