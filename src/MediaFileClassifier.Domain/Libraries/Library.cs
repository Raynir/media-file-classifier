using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Libraries;

/// <summary>
/// A searchable index of one or more folders (spec 1.2). Each library is stored in its own database,
/// so a library can be moved to another device as a whole (D004).
/// </summary>
/// <remarks>
/// Invariants: the name is not blank, the schema version is positive, and the modified date is not before the created date.
/// </remarks>
public sealed class Library
{
    /// <summary>
    /// Schema version written by this build.
    /// </summary>
    public const int CurrentSchemaVersion = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="Library"/> class from stored values.
    /// </summary>
    /// <param name="id">Library ID.</param>
    /// <param name="name">Display name.</param>
    /// <param name="createdAt">Creation date.</param>
    /// <param name="modifiedAt">Last modification date.</param>
    /// <param name="schemaVersion">Schema version of the library database.</param>
    /// <param name="settings">Per-library settings.</param>
    public Library(
        Id<Library> id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset modifiedAt,
        int schemaVersion,
        LibrarySettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(modifiedAt, createdAt);
        ArgumentNullException.ThrowIfNull(settings);

        Id = id;
        Name = name.Trim();
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
        SchemaVersion = schemaVersion;
        Settings = settings;
    }

    public Id<Library> Id { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>
    /// Gets the schema version of the library database, used to migrate libraries created by older builds.
    /// </summary>
    public int SchemaVersion { get; }

    public LibrarySettings Settings { get; private set; }

    /// <summary>
    /// Creates a new, empty library with default settings.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="now">Current time.</param>
    /// <returns>The new library.</returns>
    public static Library Create(string name, DateTimeOffset now) =>
        new(Id<Library>.New(), name, now, now, CurrentSchemaVersion, new LibrarySettings());

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Touch(now);
    }

    public void ChangeSettings(LibrarySettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        Touch(now);
    }

    private void Touch(DateTimeOffset now) => ModifiedAt = now > ModifiedAt ? now : ModifiedAt;
}
