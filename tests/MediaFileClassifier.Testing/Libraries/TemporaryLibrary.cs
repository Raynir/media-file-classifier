using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Testing.Libraries;

/// <summary>
/// A library folder in the temp folder, deleted on dispose, following the <c>&lt;name&gt;.mfc/library.db</c>
/// layout of E01-S03. <see cref="Create"/> makes an empty SQLite database; <see cref="Reserve"/> only picks the
/// paths, so the library store can create the library with its schema in <see cref="ParentDirectory"/>.
/// </summary>
public sealed class TemporaryLibrary : IDisposable
{
    /// <summary>
    /// File name of the library database inside the library folder.
    /// </summary>
    public const string DatabaseFileName = "library.db";

    private readonly string rootDirectory;

    private TemporaryLibrary(string rootDirectory, string name)
    {
        this.rootDirectory = rootDirectory;
        LibraryDirectory = Path.Combine(rootDirectory, name + ".mfc");
        DatabasePath = Path.Combine(LibraryDirectory, DatabaseFileName);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    /// <summary>
    /// Gets the unique temp folder the library folder is in.
    /// </summary>
    public string ParentDirectory => rootDirectory;

    /// <summary>
    /// Gets the library folder, e.g. <c>%TEMP%\mfc-tests\&lt;guid&gt;\test.mfc</c>.
    /// </summary>
    public string LibraryDirectory { get; }

    /// <summary>
    /// Gets the full path of the library database.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Gets the connection string of the library database.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates the library folder and its database file.
    /// </summary>
    /// <param name="name">Library name, without the <c>.mfc</c> extension.</param>
    /// <returns>The created library.</returns>
    public static TemporaryLibrary Create(string name = "test")
    {
        var library = new TemporaryLibrary(Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N")), name);
        Directory.CreateDirectory(library.LibraryDirectory);
        using (library.OpenConnection())
        {
        }

        return library;
    }

    /// <summary>
    /// Picks the paths of a library without creating anything, e.g. for <c>ILibraryStore.CreateAsync</c> with
    /// <see cref="ParentDirectory"/> and the same name.
    /// </summary>
    /// <param name="name">Library name, without the <c>.mfc</c> extension.</param>
    /// <returns>The reserved library.</returns>
    public static TemporaryLibrary Reserve(string name = "test") =>
        new(Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N")), name);

    /// <summary>
    /// Opens a new connection to the library database. The caller disposes it.
    /// </summary>
    /// <returns>An open connection.</returns>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Pooled connections keep the database file locked on Windows.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }
}
