using System.Text.Json;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores embeddings (D067) and tells the in-memory vector index about every commit through <see cref="Changed"/>.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class EmbeddingRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IEmbeddingRepository
{
    /// <inheritdoc />
    public event EventHandler<EmbeddingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public async Task ReplaceAsync(
        Id<MediaItem> itemId,
        Id<ModelRegistration> modelId,
        IReadOnlyList<Embedding> embeddings,
        CancellationToken cancellationToken = default)
    {
        var change = new ItemEmbeddings(itemId, modelId, embeddings);
        Validate(change);
        var committed = await writer.ExecuteAsync((db, token) => WriteAsync(db, [change], token), RaiseChanged, cancellationToken).ConfigureAwait(false);
        if (committed.Count == 0)
        {
            throw new KeyNotFoundException($"Media item {itemId} doesn't exist.");
        }
    }

    /// <inheritdoc />
    public async Task ReplaceRangeAsync(IReadOnlyList<ItemEmbeddings> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            Validate(change);
        }

        // The last change of an item and model wins, as if they were written one after another.
        var latest = changes
            .Select((change, order) => (change, order))
            .GroupBy(entry => (entry.change.ItemId, entry.change.ModelId))
            .Select(group => group.MaxBy(entry => entry.order))
            .OrderBy(entry => entry.order)
            .Select(entry => entry.change)
            .ToList();
        foreach (var batch in latest.Chunk(MediaItemRepository.BatchSize))
        {
            await writer.ExecuteAsync((db, token) => WriteAsync(db, batch, token), RaiseChanged, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Embedding>> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<Embedding>>(
            async (db, token) => await db.Embeddings.Where(embedding => embedding.ItemId == itemId).ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task ReadAllVectorsAsync(StoredVectorVisitor visitor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return connections.ReadAsync(
            async (db, token) =>
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();

                // The (model_id, item_id) index gives the grouping; only each item's few vectors are sorted.
                command.CommandText = """
                    SELECT e."model_id", d."doc_id", e."item_id", e."dimension", e."vector"
                    FROM "embedding" e
                    JOIN "search_document" d ON d."item_id" = e."item_id"
                    LEFT JOIN "keyframe" k ON k."id" = e."keyframe_id"
                    ORDER BY e."model_id", e."item_id", e."keyframe_id" IS NOT NULL, k."index"
                    """;
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                var buffer = Array.Empty<byte>();
                string? modelText = null;
                var modelId = default(Id<ModelRegistration>);
                string? itemText = null;
                var itemId = default(Id<MediaItem>);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var currentModel = reader.GetString(0);
                    if (currentModel != modelText)
                    {
                        modelText = currentModel;
                        modelId = Id<ModelRegistration>.Parse(currentModel);
                    }

                    var currentItem = reader.GetString(2);
                    if (currentItem != itemText)
                    {
                        itemText = currentItem;
                        itemId = Id<MediaItem>.Parse(currentItem);
                    }

                    var length = (int)reader.GetBytes(4, 0, null, 0, 0);
                    if (buffer.Length < length)
                    {
                        buffer = new byte[length];
                    }

                    reader.GetBytes(4, 0, buffer, 0, length);
                    var vector = new StoredVector(modelId, checked((int)reader.GetInt64(1)), itemId, reader.GetInt32(3));
                    visitor(in vector, buffer.AsSpan(0, length));
                }

                return true;
            },
            cancellationToken);
    }

    private static void Validate(ItemEmbeddings change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(change.Embeddings);
        foreach (var embedding in change.Embeddings)
        {
            if (embedding.ItemId != change.ItemId || embedding.ModelId != change.ModelId)
            {
                throw new ArgumentException($"Embedding {embedding.Id} doesn't belong to item {change.ItemId} and model {change.ModelId}.", nameof(change));
            }
        }
    }

    private static async Task<IReadOnlyList<CommittedEmbeddings>> WriteAsync(
        LibraryDbContext db,
        IReadOnlyList<ItemEmbeddings> changes,
        CancellationToken cancellationToken)
    {
        var documents = await ReadDocumentIdsAsync(db, changes.Select(change => change.ItemId), cancellationToken).ConfigureAwait(false);
        var keyframeIds = changes.SelectMany(change => change.Embeddings).Select(embedding => embedding.KeyframeId).OfType<Id<Keyframe>>().ToList();
        var keyframeIndexes = keyframeIds.Count == 0
            ? []
            : await db.Keyframes.AsNoTracking().Where(keyframe => keyframeIds.Contains(keyframe.Id))
                .ToDictionaryAsync(keyframe => keyframe.Id, keyframe => keyframe.Index, cancellationToken).ConfigureAwait(false);

        var committed = new List<CommittedEmbeddings>();
        foreach (var change in changes)
        {
            if (!documents.TryGetValue(change.ItemId, out var document))
            {
                continue;
            }

            await db.Embeddings.Where(embedding => embedding.ItemId == change.ItemId && embedding.ModelId == change.ModelId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // The order the index keeps and the load reads: whole item first, then keyframes in order.
            var ordered = change.Embeddings
                .OrderBy(embedding => embedding.KeyframeId is not null)
                .ThenBy(embedding => embedding.KeyframeId is { } keyframe ? keyframeIndexes.GetValueOrDefault(keyframe) : -1)
                .ToList();
            db.Embeddings.AddRange(ordered);
            committed.Add(new CommittedEmbeddings(document, change with { Embeddings = ordered }));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return committed;
    }

    private static async Task<Dictionary<Id<MediaItem>, int>> ReadDocumentIdsAsync(
        LibraryDbContext db,
        IEnumerable<Id<MediaItem>> itemIds,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT "doc_id", "item_id" FROM "search_document" WHERE "item_id" IN (SELECT "value" FROM json_each($items))
            """;
        command.Parameters.Add(new SqliteParameter("$items", JsonSerializer.Serialize(itemIds.Select(id => id.ToString()).Distinct())));
        var documents = new Dictionary<Id<MediaItem>, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            documents[Id<MediaItem>.Parse(reader.GetString(1))] = checked((int)reader.GetInt64(0));
        }

        return documents;
    }

    // Runs on the writer right after the commit, so listeners see the commits in order (D067).
    private void RaiseChanged(IReadOnlyList<CommittedEmbeddings> committed)
    {
        if (committed.Count > 0)
        {
            Changed?.Invoke(this, new EmbeddingsChangedEventArgs(committed));
        }
    }
}
