namespace MediaFileClassifier.Benchmarks.Data;

/// <summary>
/// The SQL of the synthetic library. It is a benchmark schema, not the production one (MFC-19),
/// but it has the same shape: items with their text, and embeddings in a table of their own.
/// </summary>
internal static class LibrarySchema
{
    public const string CreateTables = """
        CREATE TABLE media_items (
            id INTEGER PRIMARY KEY,
            library_id INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            path TEXT NOT NULL,
            file_name TEXT NOT NULL,
            description TEXT NOT NULL,
            ocr_text TEXT,
            tags TEXT NOT NULL,
            taken_at INTEGER NOT NULL,
            size_bytes INTEGER NOT NULL
        );
        CREATE INDEX ix_media_items_filters ON media_items (library_id, kind, taken_at);

        -- Float32 embeddings, L2-normalized. One row per item, keyframe or model version.
        CREATE TABLE embeddings (
            id INTEGER PRIMARY KEY,
            item_id INTEGER NOT NULL REFERENCES media_items (id),
            model_id INTEGER NOT NULL,
            frame_index INTEGER NOT NULL,
            vector BLOB NOT NULL
        );
        CREATE INDEX ix_embeddings_item ON embeddings (item_id);

        -- The same vectors in half precision, for the in-memory half engines.
        CREATE TABLE embeddings_f16 (
            id INTEGER PRIMARY KEY,
            item_id INTEGER NOT NULL,
            vector BLOB NOT NULL
        );

        CREATE TABLE queries (
            id INTEGER PRIMARY KEY,
            vector BLOB NOT NULL
        );

        CREATE TABLE library_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

    /// <summary>
    /// The sqlite-vec table. The filter columns are vec0 metadata columns, so the KNN scan filters
    /// as it goes. item_id is an auxiliary column, which is only returned.
    /// </summary>
    public const string CreateVecTable = """
        CREATE VIRTUAL TABLE vec_embeddings USING vec0 (
            embedding float[{0}] distance_metric=cosine,
            library_id integer,
            kind integer,
            taken_at integer,
            +item_id integer
        );
        """;

    public const string FillVecTable = """
        INSERT INTO vec_embeddings (rowid, embedding, library_id, kind, taken_at, item_id)
        SELECT e.id, e.vector, m.library_id, m.kind, m.taken_at, e.item_id
        FROM embeddings e
        JOIN media_items m ON m.id = e.item_id
        ORDER BY e.id;
        """;

    public const string InsertItem = """
        INSERT INTO media_items (id, library_id, kind, path, file_name, description, ocr_text, tags, taken_at, size_bytes)
        VALUES (@id, @library_id, @kind, @path, @file_name, @description, @ocr_text, @tags, @taken_at, @size_bytes);
        """;

    public const string InsertEmbedding = """
        INSERT INTO embeddings (id, item_id, model_id, frame_index, vector)
        VALUES (@id, @item_id, @model_id, @frame_index, @vector);
        """;

    public const string InsertEmbeddingHalf = "INSERT INTO embeddings_f16 (id, item_id, vector) VALUES (@id, @item_id, @vector);";

    public const string InsertQuery = "INSERT INTO queries (id, vector) VALUES (@id, @vector);";

    public const string InsertInfo = "INSERT OR REPLACE INTO library_info (key, value) VALUES (@key, @value);";

    public const string SelectInfo = "SELECT key, value FROM library_info ORDER BY key;";

    public const string SelectQueries = "SELECT vector FROM queries ORDER BY id LIMIT @limit;";

    public const string SelectVectors = "SELECT item_id, vector FROM embeddings ORDER BY item_id, id;";

    public const string SelectHalfVectors = "SELECT item_id, vector FROM embeddings_f16 ORDER BY item_id, id;";

    /// <summary>
    /// 2020-01-01T00:00:00Z, the lower bound of the broad filter.
    /// </summary>
    public const long Year2020 = 1_577_836_800;

    /// <summary>
    /// The ids of the items that pass a filter. The in-memory engines run it before scoring.
    /// </summary>
    public static string? FilteredItemIds(FilterScenario filter) => filter switch
    {
        FilterScenario.None => null,
        FilterScenario.Broad => "SELECT id FROM media_items WHERE library_id = 1 AND kind = 0 AND taken_at >= 1577836800;",
        FilterScenario.Narrow => "SELECT id FROM media_items WHERE library_id = 2 AND kind = 1;",
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    /// <summary>
    /// sqlite-vec KNN over the vec0 table. Filters are vec0 metadata constraints.
    /// </summary>
    public static string VecKnn(FilterScenario filter) => filter switch
    {
        FilterScenario.None => """
            SELECT item_id, distance FROM vec_embeddings
            WHERE embedding MATCH @query AND k = @k
            ORDER BY distance;
            """,
        FilterScenario.Broad => """
            SELECT item_id, distance FROM vec_embeddings
            WHERE embedding MATCH @query AND k = @k
              AND library_id = 1 AND kind = 0 AND taken_at >= 1577836800
            ORDER BY distance;
            """,
        FilterScenario.Narrow => """
            SELECT item_id, distance FROM vec_embeddings
            WHERE embedding MATCH @query AND k = @k
              AND library_id = 2 AND kind = 1
            ORDER BY distance;
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    /// <summary>
    /// sqlite-vec's scalar distance function over the plain embeddings table: an exact scan that
    /// can join and filter on anything SQL can express.
    /// </summary>
    public static string VecScan(FilterScenario filter) => filter switch
    {
        FilterScenario.None => """
            SELECT item_id, MIN(vec_distance_cosine(vector, @query)) AS distance
            FROM embeddings
            GROUP BY item_id
            ORDER BY distance
            LIMIT @k;
            """,
        FilterScenario.Broad => """
            SELECT e.item_id, MIN(vec_distance_cosine(e.vector, @query)) AS distance
            FROM media_items m
            JOIN embeddings e ON e.item_id = m.id
            WHERE m.library_id = 1 AND m.kind = 0 AND m.taken_at >= 1577836800
            GROUP BY e.item_id
            ORDER BY distance
            LIMIT @k;
            """,
        FilterScenario.Narrow => """
            SELECT e.item_id, MIN(vec_distance_cosine(e.vector, @query)) AS distance
            FROM media_items m
            JOIN embeddings e ON e.item_id = m.id
            WHERE m.library_id = 2 AND m.kind = 1
            GROUP BY e.item_id
            ORDER BY distance
            LIMIT @k;
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };
}
