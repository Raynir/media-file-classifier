using System.Numerics.Tensors;
using MediaFileClassifier.Benchmarks.Data;
using MediaFileClassifier.Benchmarks.Engines;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Benchmarks.Checks;

/// <summary>
/// The yes/no questions of the spike: can the SQLitePCLRaw bundle load extensions, do filters
/// combine with the vector query, and when does a write become visible to search.
/// </summary>
internal static class SpikeChecks
{
    private const int Dimensions = 512;

    public static int Run(string dataDirectory)
    {
        var checks = new CheckList();
        var spec = new LibrarySpec(2_000, 2, Dimensions, Seed: 5);
        var path = LibraryGenerator.Ensure(spec, dataDirectory, rebuild: true, Console.Out);

        Console.WriteLine();
        Console.WriteLine("SQLite and extension loading");
        CheckExtensionLoading(checks, path);

        Console.WriteLine();
        Console.WriteLine("Filters combined with the vector query");
        CheckFilters(checks, path);

        Console.WriteLine();
        Console.WriteLine("Transactions: sqlite-vec");
        CheckVecTransactions(checks, path, spec);

        Console.WriteLine();
        Console.WriteLine("Transactions: in-memory index");
        CheckMemoryTransactions(checks, path, spec);

        Console.WriteLine();
        Console.WriteLine(checks.Failed == 0 ? $"All {checks.Passed} checks passed." : $"{checks.Failed} of {checks.Passed + checks.Failed} checks FAILED.");
        return checks.Failed == 0 ? 0 : 1;
    }

    private static void CheckExtensionLoading(CheckList checks, string path)
    {
        using var connection = SqliteDatabase.Open(path, loadVec: false);
        Console.WriteLine($"  Provider: {SQLitePCL.raw.GetNativeLibraryName()}, SQLite {SqliteDatabase.Scalar<string>(connection, "SELECT sqlite_version();")}");
        var options = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA compile_options;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                options.Add(reader.GetString(0));
            }
        }

        checks.Check("The bundle is built without SQLITE_OMIT_LOAD_EXTENSION", !options.Contains("OMIT_LOAD_EXTENSION"));

        var sqlLoadBlocked = false;
        try
        {
            SqliteDatabase.Execute(connection, "SELECT load_extension('vec0');");
        }
        catch (SqliteException)
        {
            sqlLoadBlocked = true;
        }

        checks.Check("SQL load_extension() is disabled unless the app enables it", sqlLoadBlocked);

        SqliteDatabase.LoadVec(connection);
        var version = SqliteDatabase.Scalar<string>(connection, "SELECT vec_version();");
        checks.Check($"SqliteConnection.LoadExtension(\"vec0\") loads sqlite-vec {version}", version is not null);
    }

    // Every engine must return only items that pass the filter, and the same items as the exact
    // float32 baseline.
    private static void CheckFilters(CheckList checks, string path)
    {
        using var connection = SqliteDatabase.Open(path, loadVec: false, readOnly: true);
        var queries = SqliteDatabase.ReadQueries(connection, 10, Dimensions, out var count);
        using var reference = (InMemoryEngine)EngineCatalog.Create(EngineCatalog.Reference, 2);
        reference.Load(path, Dimensions);
        foreach (var name in new[] { "sqlitevec-knn", "sqlitevec-scan", "memory-f16-dot" })
        {
            using var engine = EngineCatalog.Create(name, 2);
            engine.Load(path, Dimensions);
            foreach (var filter in new[] { FilterScenario.Broad, FilterScenario.Narrow })
            {
                var passing = PassingIds(connection, filter);
                var allPass = true;
                var same = 0;
                var total = 0;
                for (var q = 0; q < count; q++)
                {
                    var query = queries.AsSpan(q * Dimensions, Dimensions);
                    var hits = engine.Search(query, filter, 20);
                    var expected = reference.Search(query, filter, 20);
                    allPass &= hits.All(hit => passing.Contains(hit.ItemId));
                    same += hits.Select(h => h.ItemId).Intersect(expected.Select(h => h.ItemId)).Count();
                    total += expected.Count;
                }

                checks.Check($"{name}, {filter} filter: every result passes the filter", allPass);
                checks.Check($"{name}, {filter} filter: {same}/{total} results match the exact baseline", same >= total * 0.99);
            }
        }
    }

    private static void CheckVecTransactions(CheckList checks, string path, LibrarySpec spec)
    {
        using var writer = SqliteDatabase.Open(path, loadVec: true);
        using var reader = SqliteDatabase.Open(path, loadVec: true);
        var probe = ProbeVector();
        var itemId = spec.Items + 1L;
        var vectorId = spec.Vectors + 1L;

        using (var transaction = writer.BeginTransaction())
        {
            InsertProbe(writer, transaction, itemId, vectorId, probe, withVec: true);
            checks.Check("The writing connection sees its own uncommitted vector", TopVecItem(writer, transaction, probe) == itemId);
            checks.Check("Other connections don't see an uncommitted vector (WAL snapshot)", TopVecItem(reader, null, probe) != itemId);
            transaction.Rollback();
        }

        checks.Check("After a rollback the vector is gone everywhere", TopVecItem(writer, null, probe) != itemId && TopVecItem(reader, null, probe) != itemId);

        using (var transaction = writer.BeginTransaction())
        {
            InsertProbe(writer, transaction, itemId, vectorId, probe, withVec: true);
            transaction.Commit();
        }

        checks.Check("A committed vector is visible to other connections at once", TopVecItem(reader, null, probe) == itemId);
        checks.Check(
            "The BLOB row and the vec0 row were committed together",
            SqliteDatabase.Scalar<long>(reader, $"SELECT (SELECT COUNT(*) FROM embeddings WHERE id = {vectorId}) + (SELECT COUNT(*) FROM vec_embeddings WHERE rowid = {vectorId});") == 2);

        using (var transaction = writer.BeginTransaction())
        {
            SqliteDatabase.Execute(writer, $"DELETE FROM vec_embeddings WHERE rowid = {vectorId}; DELETE FROM embeddings WHERE id = {vectorId}; DELETE FROM media_items WHERE id = {itemId};", transaction);
            transaction.Commit();
        }

        checks.Check("A committed delete is visible at once", TopVecItem(reader, null, probe) != itemId);
    }

    private static void CheckMemoryTransactions(CheckList checks, string path, LibrarySpec spec)
    {
        using var engine = (InMemoryEngine)EngineCatalog.Create("memory-f32-dot", spec.VectorsPerItem);
        engine.Load(path, Dimensions);
        using var writer = SqliteDatabase.Open(path, loadVec: true);
        var probe = ProbeVector();
        var itemId = spec.Items + 2L;
        var vectorId = spec.Vectors + 2L;

        using (var transaction = writer.BeginTransaction())
        {
            InsertProbe(writer, transaction, itemId, vectorId, probe, withVec: false);
            checks.Check("The index doesn't see a vector before its transaction commits", Top(engine, probe) != itemId);
            transaction.Rollback();
        }

        checks.Check("After a rollback there is nothing to undo in the index", Top(engine, probe) != itemId);

        // The production pattern: apply the change to the index after the commit succeeds.
        using (var transaction = writer.BeginTransaction())
        {
            InsertProbe(writer, transaction, itemId, vectorId, probe, withVec: false);
            transaction.Commit();
        }

        checks.Check("Before the post-commit update, search misses the committed vector", Top(engine, probe) != itemId);
        engine.Index.Upsert(itemId, probe);
        checks.Check("After the post-commit update, search finds it", Top(engine, probe) == itemId);

        using var reloaded = (InMemoryEngine)EngineCatalog.Create("memory-f32-dot", spec.VectorsPerItem);
        reloaded.Load(path, Dimensions);
        checks.Check("Reloading from the database gives the same index (count and top hit)", reloaded.Index.Count == engine.Index.Count && Top(reloaded, probe) == itemId);

        SqliteDatabase.Execute(writer, $"DELETE FROM embeddings WHERE id = {vectorId}; DELETE FROM media_items WHERE id = {itemId};");
        engine.Index.Remove(itemId);
        checks.Check("A removed item is not returned", Top(engine, probe) != itemId);
    }

    private static float[] ProbeVector()
    {
        var random = new Random(99);
        var vector = new float[Dimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(random.NextDouble() - 0.5);
        }

        TensorPrimitives.Divide(vector, TensorPrimitives.Norm(vector), vector);
        return vector;
    }

    private static void InsertProbe(SqliteConnection connection, SqliteTransaction transaction, long itemId, long vectorId, float[] probe, bool withVec)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = withVec
            ? """
              INSERT INTO media_items SELECT @item, library_id, kind, path, file_name, description, ocr_text, tags, taken_at, size_bytes FROM media_items WHERE id = 1;
              INSERT INTO embeddings (id, item_id, model_id, frame_index, vector) VALUES (@vector_id, @item, 1, 0, @vector);
              INSERT INTO vec_embeddings (rowid, embedding, library_id, kind, taken_at, item_id)
              SELECT @vector_id, @vector, library_id, kind, taken_at, id FROM media_items WHERE id = @item;
              """
            : """
              INSERT INTO media_items SELECT @item, library_id, kind, path, file_name, description, ocr_text, tags, taken_at, size_bytes FROM media_items WHERE id = 1;
              INSERT INTO embeddings (id, item_id, model_id, frame_index, vector) VALUES (@vector_id, @item, 1, 0, @vector);
              """;
        command.Parameters.AddWithValue("@item", itemId);
        command.Parameters.AddWithValue("@vector_id", vectorId);
        command.Parameters.AddWithValue("@vector", SqliteDatabase.ToBlob(probe));
        command.ExecuteNonQuery();
    }

    private static long TopVecItem(SqliteConnection connection, SqliteTransaction? transaction, float[] probe)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LibrarySchema.VecKnn(FilterScenario.None);
        command.Parameters.AddWithValue("@query", SqliteDatabase.ToBlob(probe));
        command.Parameters.AddWithValue("@k", 1);
        return command.ExecuteScalar() is long id ? id : -1;
    }

    private static long Top(InMemoryEngine engine, float[] probe) =>
        engine.Search(probe, FilterScenario.None, 1) is [var hit] ? hit.ItemId : -1;

    private static HashSet<long> PassingIds(SqliteConnection connection, FilterScenario filter)
    {
        var ids = new HashSet<long>();
        using var command = connection.CreateCommand();
        command.CommandText = LibrarySchema.FilteredItemIds(filter);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private sealed class CheckList
    {
        public int Passed { get; private set; }

        public int Failed { get; private set; }

        public void Check(string description, bool ok)
        {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {description}");
            if (ok)
            {
                Passed++;
            }
            else
            {
                Failed++;
            }
        }
    }
}
