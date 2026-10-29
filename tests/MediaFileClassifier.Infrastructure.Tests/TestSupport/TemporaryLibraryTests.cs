using MediaFileClassifier.Testing.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.TestSupport;

/// <summary>
/// Tests for <see cref="TemporaryLibrary"/>.
/// </summary>
public sealed class TemporaryLibraryTests
{
    [Fact]
    public void Create_MakesDatabaseInMfcFolder()
    {
        using var library = TemporaryLibrary.Create("Holidays");

        Assert.Equal("Holidays.mfc", Path.GetFileName(library.LibraryDirectory));
        Assert.Equal(Path.Combine(library.LibraryDirectory, "library.db"), library.DatabasePath);
        Assert.True(File.Exists(library.DatabasePath));
    }

    [Fact]
    public void OpenConnection_DatabaseIsWritable()
    {
        using var library = TemporaryLibrary.Create();

        using (var connection = library.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE item (name TEXT NOT NULL); INSERT INTO item VALUES ('a'), ('b');";
            command.ExecuteNonQuery();
        }

        using (var connection = library.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM item;";
            Assert.Equal(2L, command.ExecuteScalar());
        }
    }

    [Fact]
    public void Dispose_DeletesLibraryFolder()
    {
        var library = TemporaryLibrary.Create();
        using (library.OpenConnection())
        {
        }

        library.Dispose();

        Assert.False(Directory.Exists(library.LibraryDirectory));
    }
}
