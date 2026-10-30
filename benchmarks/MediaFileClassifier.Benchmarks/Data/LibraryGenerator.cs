using System.Diagnostics;
using System.Globalization;
using System.Numerics.Tensors;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// Builds a synthetic library database for a <see cref="LibrarySpec"/>, with the float32 and half
/// BLOB tables and the sqlite-vec vec0 table side by side, so every engine reads the same data.
/// </summary>
internal static class LibraryGenerator
{
    private static readonly DateTimeOffset FirstDate = new(2008, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastDate = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Returns the database for <paramref name="spec"/>, building it unless it already exists.
    /// </summary>
    public static string Ensure(LibrarySpec spec, string directory, bool rebuild, TextWriter log)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, spec.FileName);
        if (!rebuild && File.Exists(path) && IsComplete(path))
        {
            log.WriteLine($"Using {path}");
            return path;
        }

        Build(spec, path, log);
        return path;
    }

    public static void Build(LibrarySpec spec, string path, TextWriter log)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }

        log.WriteLine($"Generating {spec.Name}: {spec.Items:N0} items, {spec.Vectors:N0} vectors -> {path}");
        var random = new Random(spec.Seed);
        var text = new SyntheticText(random);
        var embeddings = new SyntheticEmbeddings(random, spec.Dimensions);

        using var connection = SqliteDatabase.Open(path, loadVec: true);
        SqliteDatabase.Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA synchronous = OFF;");
        SqliteDatabase.Execute(connection, LibrarySchema.CreateTables);

        var stopwatch = Stopwatch.StartNew();
        using (var transaction = connection.BeginTransaction())
        {
            InsertItems(connection, transaction, spec, random, text, embeddings);
            InsertQueries(connection, transaction, spec, embeddings);
            transaction.Commit();
        }

        var baseInsertMs = stopwatch.ElapsedMilliseconds;
        log.WriteLine($"  items and BLOBs: {baseInsertMs:N0} ms");

        stopwatch.Restart();
        using (var transaction = connection.BeginTransaction())
        {
            SqliteDatabase.Execute(
                connection,
                string.Format(CultureInfo.InvariantCulture, LibrarySchema.CreateVecTable, spec.Dimensions),
                transaction);
            SqliteDatabase.Execute(connection, LibrarySchema.FillVecTable, transaction);
            transaction.Commit();
        }

        var vecBuildMs = stopwatch.ElapsedMilliseconds;
        log.WriteLine($"  vec0 table: {vecBuildMs:N0} ms");

        SqliteDatabase.Execute(connection, "ANALYZE; PRAGMA wal_checkpoint(TRUNCATE);");
        var info = new Dictionary<string, string>
        {
            ["items"] = Invariant(spec.Items),
            ["vectors_per_item"] = Invariant(spec.VectorsPerItem),
            ["dimensions"] = Invariant(spec.Dimensions),
            ["seed"] = Invariant(spec.Seed),
            ["base_insert_ms"] = Invariant(baseInsertMs),
            ["vec0_build_ms"] = Invariant(vecBuildMs),
            ["sqlite_version"] = SqliteDatabase.Scalar<string>(connection, "SELECT sqlite_version();") ?? "?",
            ["sqlite_vec_version"] = SqliteDatabase.Scalar<string>(connection, "SELECT vec_version();") ?? "?",
            ["text_bytes"] = Invariant(SqliteDatabase.Scalar<long>(
                connection,
                "SELECT SUM(LENGTH(path) + LENGTH(file_name) + LENGTH(description) + IFNULL(LENGTH(ocr_text), 0) + LENGTH(tags)) FROM media_items;")),
            ["complete"] = "1",
        };
        WriteInfo(connection, info);
    }

    public static bool IsComplete(string path)
    {
        try
        {
            using var connection = SqliteDatabase.Open(path, loadVec: false, readOnly: true);
            return SqliteDatabase.ReadInfo(connection).ContainsKey("complete");
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LibrarySpec spec,
        Random random,
        SyntheticText text,
        SyntheticEmbeddings embeddings)
    {
        using var item = connection.CreateCommand();
        item.Transaction = transaction;
        item.CommandText = LibrarySchema.InsertItem;
        var itemId = item.Parameters.Add("@id", SqliteType.Integer);
        var libraryId = item.Parameters.Add("@library_id", SqliteType.Integer);
        var kind = item.Parameters.Add("@kind", SqliteType.Integer);
        var path = item.Parameters.Add("@path", SqliteType.Text);
        var fileName = item.Parameters.Add("@file_name", SqliteType.Text);
        var description = item.Parameters.Add("@description", SqliteType.Text);
        var ocrText = item.Parameters.Add("@ocr_text", SqliteType.Text);
        var tags = item.Parameters.Add("@tags", SqliteType.Text);
        var takenAt = item.Parameters.Add("@taken_at", SqliteType.Integer);
        var sizeBytes = item.Parameters.Add("@size_bytes", SqliteType.Integer);

        using var vector = connection.CreateCommand();
        vector.Transaction = transaction;
        vector.CommandText = LibrarySchema.InsertEmbedding;
        var vectorId = vector.Parameters.Add("@id", SqliteType.Integer);
        var vectorItemId = vector.Parameters.Add("@item_id", SqliteType.Integer);
        var modelId = vector.Parameters.Add("@model_id", SqliteType.Integer);
        var frameIndex = vector.Parameters.Add("@frame_index", SqliteType.Integer);
        var vectorBlob = vector.Parameters.Add("@vector", SqliteType.Blob);

        using var half = connection.CreateCommand();
        half.Transaction = transaction;
        half.CommandText = LibrarySchema.InsertEmbeddingHalf;
        var halfId = half.Parameters.Add("@id", SqliteType.Integer);
        var halfItemId = half.Parameters.Add("@item_id", SqliteType.Integer);
        var halfBlob = half.Parameters.Add("@vector", SqliteType.Blob);

        var span = (LastDate - FirstDate).TotalSeconds;
        var single = new float[spec.Dimensions];
        var halves = new Half[spec.Dimensions];
        long nextVectorId = 1;
        for (var id = 1; id <= spec.Items; id++)
        {
            var mediaKind = random.Next(100) switch
            {
                < 70 => MediaKind.Photo,
                < 90 => MediaKind.Video,
                _ => MediaKind.AnimatedImage,
            };
            var taken = FirstDate.AddSeconds(random.NextDouble() * span);
            var itemPath = text.Path(mediaKind, taken);

            itemId.Value = id;
            libraryId.Value = random.Next(100) switch
            {
                < 60 => 1,
                < 90 => 2,
                _ => 3,
            };
            kind.Value = (int)mediaKind;
            path.Value = itemPath;
            fileName.Value = Path.GetFileName(itemPath.Replace('\\', Path.DirectorySeparatorChar));
            description.Value = text.Description();
            ocrText.Value = (object?)text.OcrText() ?? DBNull.Value;
            tags.Value = text.Tags();
            takenAt.Value = taken.ToUnixTimeSeconds();
            sizeBytes.Value = mediaKind == MediaKind.Video ? random.NextInt64(5_000_000, 2_000_000_000) : random.Next(200_000, 15_000_000);
            item.ExecuteNonQuery();

            embeddings.NextItem();
            for (var v = 0; v < spec.VectorsPerItem; v++)
            {
                embeddings.NextItemVector(single);
                vectorId.Value = nextVectorId;
                vectorItemId.Value = id;
                modelId.Value = 1;
                frameIndex.Value = v;
                vectorBlob.Value = SqliteDatabase.ToBlob(single);
                vector.ExecuteNonQuery();

                TensorPrimitives.ConvertToHalf(single, halves);
                halfId.Value = nextVectorId;
                halfItemId.Value = id;
                halfBlob.Value = SqliteDatabase.ToBlob(halves);
                half.ExecuteNonQuery();
                nextVectorId++;
            }
        }
    }

    private static void InsertQueries(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LibrarySpec spec,
        SyntheticEmbeddings embeddings)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LibrarySchema.InsertQuery;
        var id = command.Parameters.Add("@id", SqliteType.Integer);
        var blob = command.Parameters.Add("@vector", SqliteType.Blob);
        var query = new float[spec.Dimensions];
        for (var i = 1; i <= LibrarySpec.StoredQueries; i++)
        {
            embeddings.NextQuery(query);
            id.Value = i;
            blob.Value = SqliteDatabase.ToBlob(query);
            command.ExecuteNonQuery();
        }
    }

    private static void WriteInfo(SqliteConnection connection, Dictionary<string, string> info)
    {
        using var command = connection.CreateCommand();
        command.CommandText = LibrarySchema.InsertInfo;
        var key = command.Parameters.Add("@key", SqliteType.Text);
        var value = command.Parameters.Add("@value", SqliteType.Text);
        foreach (var (name, text) in info)
        {
            key.Value = name;
            value.Value = text;
            command.ExecuteNonQuery();
        }
    }

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
