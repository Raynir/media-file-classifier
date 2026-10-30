using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// Opens benchmark databases and moves vectors in and out of BLOB columns.
/// </summary>
internal static class SqliteDatabase
{
    /// <summary>
    /// The sqlite-vec loadable extension, as shipped in the sqlite-vec NuGet package
    /// (runtimes/&lt;rid&gt;/native/vec0.dll). Microsoft.Data.Sqlite finds it through deps.json.
    /// </summary>
    public const string VecExtension = "vec0";

    public static SqliteConnection Open(string path, bool loadVec, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        if (loadVec)
        {
            LoadVec(connection);
        }

        return connection;
    }

    public static void LoadVec(SqliteConnection connection)
    {
        connection.EnableExtensions(true);
        connection.LoadExtension(VecExtension);
        connection.EnableExtensions(false);
    }

    public static int Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command.ExecuteNonQuery();
    }

    public static T? Scalar<T>(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    public static byte[] ToBlob(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    public static byte[] ToBlob(ReadOnlySpan<Half> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    public static float[] ReadQueries(SqliteConnection connection, int count, int dimensions, out int read)
    {
        var queries = new float[count * dimensions];
        using var command = connection.CreateCommand();
        command.CommandText = LibrarySchema.SelectQueries;
        command.Parameters.AddWithValue("@limit", count);
        using var reader = command.ExecuteReader();
        var buffer = new byte[dimensions * sizeof(float)];
        read = 0;
        while (reader.Read())
        {
            ReadBlob(reader, 0, buffer, MemoryMarshal.AsBytes(queries.AsSpan(read * dimensions, dimensions)));
            read++;
        }

        return queries;
    }

    /// <summary>
    /// Copies a BLOB column into <paramref name="destination"/>, which must be its exact size.
    /// <paramref name="buffer"/> is reused between rows to avoid an allocation per row.
    /// </summary>
    public static void ReadBlob(SqliteDataReader reader, int ordinal, byte[] buffer, Span<byte> destination)
    {
        var length = reader.GetBytes(ordinal, 0, buffer, 0, destination.Length);
        if (length != destination.Length)
        {
            throw new InvalidDataException($"Expected a {destination.Length}-byte vector, got {length} bytes.");
        }

        buffer.AsSpan(0, destination.Length).CopyTo(destination);
    }

    public static Dictionary<string, string> ReadInfo(SqliteConnection connection)
    {
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = LibrarySchema.SelectInfo;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            info[reader.GetString(0)] = reader.GetString(1);
        }

        return info;
    }
}
