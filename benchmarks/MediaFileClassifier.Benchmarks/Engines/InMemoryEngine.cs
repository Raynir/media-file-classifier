using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MediaFileClassifier.Benchmarks.Data;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// Prototype B as an engine: loads the BLOB embeddings into an <see cref="InMemoryVectorIndex"/>
/// and runs filters as plain SQL on the items table before scoring.
/// </summary>
internal sealed class InMemoryEngine(string name, VectorPrecision precision, ScoreFunction function, int parallelism)
    : IVectorSearchEngine
{
    private SqliteConnection? connection;
    private InMemoryVectorIndex? index;

    public string Name => name;

    public InMemoryVectorIndex Index => index ?? throw new InvalidOperationException("Load the engine first.");

    public void Load(string databasePath, int dimensions)
    {
        connection = SqliteDatabase.Open(databasePath, loadVec: false, readOnly: true);
        var vectors = SqliteDatabase.Scalar<int>(connection, "SELECT COUNT(*) FROM embeddings;");
        var loaded = new InMemoryVectorIndex(dimensions, precision, vectors);
        if (precision == VectorPrecision.Half)
        {
            Fill<Half>(connection, LibrarySchema.SelectHalfVectors, dimensions, (id, span) => loaded.Upsert(id, span));
        }
        else
        {
            Fill<float>(connection, LibrarySchema.SelectVectors, dimensions, (id, span) => loaded.Upsert(id, span));
        }

        index = loaded;
    }

    public IReadOnlyList<SearchHit> Search(ReadOnlySpan<float> query, FilterScenario filter, int k)
    {
        var mask = LibrarySchema.FilteredItemIds(filter) is { } sql ? Index.CreateMask(ReadIds(sql)) : null;
        return Index.Search(query, k, function, mask, parallelism);
    }

    public void Dispose() => connection?.Dispose();

    // Reads rows ordered by item and hands each item's vectors over in one call.
    private static void Fill<T>(SqliteConnection connection, string sql, int dimensions, SpanAction<T> upsert)
        where T : unmanaged
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var vectorBytes = dimensions * Unsafe.SizeOf<T>();
        var buffer = new byte[vectorBytes];
        var pending = new T[dimensions * 16];
        var pendingCount = 0;
        var currentItem = -1L;
        while (reader.Read())
        {
            var itemId = reader.GetInt64(0);
            if (itemId != currentItem && pendingCount > 0)
            {
                upsert(currentItem, pending.AsSpan(0, pendingCount * dimensions));
                pendingCount = 0;
            }

            currentItem = itemId;
            if ((pendingCount + 1) * dimensions > pending.Length)
            {
                Array.Resize(ref pending, pending.Length * 2);
            }

            SqliteDatabase.ReadBlob(reader, 1, buffer, MemoryMarshal.AsBytes(pending.AsSpan(pendingCount * dimensions, dimensions)));
            pendingCount++;
        }

        if (pendingCount > 0)
        {
            upsert(currentItem, pending.AsSpan(0, pendingCount * dimensions));
        }
    }

    private List<long> ReadIds(string sql)
    {
        using var command = connection!.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private delegate void SpanAction<T>(long itemId, ReadOnlySpan<T> vectors);
}
