using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Testing.Libraries;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="LibraryConnectionFactory"/>: pragmas and FTS5 (E01-S03-T03).
/// </summary>
public sealed class LibraryConnectionFactoryTests : IDisposable
{
    private readonly TemporaryLibrary temporary = TemporaryLibrary.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("journal_mode", "wal")]
    [InlineData("synchronous", 1L)]
    [InlineData("foreign_keys", 1L)]
    [InlineData("busy_timeout", (long)LibraryConnectionFactory.BusyTimeoutMilliseconds)]
    public async Task OpenAsync_SetsPragma(string pragma, object expected)
    {
        await using var connection = await new LibraryConnectionFactory(temporary.DatabasePath).OpenAsync(Token);

        Assert.Equal(expected, await QueryAsync(connection, $"PRAGMA {pragma}"));
    }

    [Fact]
    public async Task OpenAsync_PooledConnection_KeepsPragmas()
    {
        var factory = new LibraryConnectionFactory(temporary.DatabasePath);
        await (await factory.OpenAsync(Token)).DisposeAsync();

        await using var connection = await factory.OpenAsync(Token);

        Assert.Equal(1L, await QueryAsync(connection, "PRAGMA foreign_keys"));
        Assert.Equal("wal", await QueryAsync(connection, "PRAGMA journal_mode"));
    }

    [Fact]
    public async Task BundledSqlite_IsCompiledWithFts5()
    {
        await using var connection = await new LibraryConnectionFactory(temporary.DatabasePath).OpenAsync(Token);

        Assert.Equal(1L, await QueryAsync(connection, "SELECT sqlite_compileoption_used('ENABLE_FTS5')"));
    }

    [Fact]
    public async Task Fts5Table_CanBeCreatedAndSearched()
    {
        await using var connection = await new LibraryConnectionFactory(temporary.DatabasePath).OpenAsync(Token);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE VIRTUAL TABLE item_text USING fts5(title, description, tokenize = 'unicode61 remove_diacritics 2');
                INSERT INTO item_text (rowid, title, description) VALUES
                    (1, 'Dog on a beach', 'A brown dog runs along the shore.'),
                    (2, 'Café in Paris', 'Two cups of coffee on a table.');
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        Assert.Equal(1L, await QueryAsync(connection, "SELECT rowid FROM item_text WHERE item_text MATCH 'shore'"));
        Assert.Equal(2L, await QueryAsync(connection, "SELECT rowid FROM item_text WHERE item_text MATCH 'cafe'"));
    }

    public void Dispose() => temporary.Dispose();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "The tests pass constant SQL.")]
    private static async Task<object?> QueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Token);
    }
}
