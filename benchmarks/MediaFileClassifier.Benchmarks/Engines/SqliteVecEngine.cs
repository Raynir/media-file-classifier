using System.Runtime.InteropServices;
using MediaFileClassifier.Benchmarks.Data;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Benchmarks.Engines;

/// <summary>
/// How the sqlite-vec engine queries.
/// </summary>
internal enum SqliteVecMode
{
    /// <summary>
    /// KNN on the vec0 virtual table, filtering on vec0 metadata columns.
    /// </summary>
    Knn,

    /// <summary>
    /// vec_distance_cosine() over the plain BLOB table, with ordinary SQL joins and filters.
    /// </summary>
    Scan,
}

/// <summary>
/// Prototype A: vector search inside SQL with the sqlite-vec extension. Both modes are exact
/// (sqlite-vec 0.1.x has no ANN index); they differ in where the vectors live and how filters apply.
/// </summary>
internal sealed class SqliteVecEngine(string name, SqliteVecMode mode, int vectorsPerItem, bool tuned) : IVectorSearchEngine
{
    // SQLite's defaults (a 2 MB page cache, no memory mapping) make every query re-read the vector
    // pages through the OS. The tuned variant maps the file and allows a 1 GB cache instead.
    private const string TuningPragmas = "PRAGMA mmap_size = 4294967296; PRAGMA cache_size = -1048576;";

    private readonly Dictionary<FilterScenario, SqliteCommand> commands = [];
    private SqliteConnection? connection;

    public string Name => name;

    public void Load(string databasePath, int dimensions)
    {
        connection = SqliteDatabase.Open(databasePath, loadVec: true, readOnly: true);
        if (tuned)
        {
            SqliteDatabase.Execute(connection, TuningPragmas);
        }

        foreach (var filter in Enum.GetValues<FilterScenario>())
        {
            var command = connection.CreateCommand();
            command.CommandText = mode == SqliteVecMode.Knn ? LibrarySchema.VecKnn(filter) : LibrarySchema.VecScan(filter);
            command.Parameters.Add("@query", SqliteType.Blob);
            command.Parameters.Add("@k", SqliteType.Integer);
            command.Prepare();
            commands[filter] = command;
        }
    }

    public IReadOnlyList<SearchHit> Search(ReadOnlySpan<float> query, FilterScenario filter, int k)
    {
        var command = commands[filter];
        command.Parameters["@query"].Value = MemoryMarshal.AsBytes(query).ToArray();

        // vec0 returns vectors, not items. The k * vectorsPerItem nearest vectors always contain the
        // k best items: each item ranked above another contributes at most vectorsPerItem vectors.
        command.Parameters["@k"].Value = mode == SqliteVecMode.Knn ? k * vectorsPerItem : k;
        using var reader = command.ExecuteReader();
        var hits = new List<SearchHit>(k);
        var seen = new HashSet<long>();
        while (reader.Read() && hits.Count < k)
        {
            var itemId = reader.GetInt64(0);
            if (seen.Add(itemId))
            {
                // sqlite-vec's cosine distance is 1 - cosine similarity.
                hits.Add(new SearchHit(itemId, 1f - reader.GetFloat(1)));
            }
        }

        return hits;
    }

    public void Dispose()
    {
        foreach (var command in commands.Values)
        {
            command.Dispose();
        }

        connection?.Dispose();
    }
}
