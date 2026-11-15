using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Repositories;

/// <summary>
/// Stores <see cref="AnalyzerState"/> rows. A run is applied with the same load-change-replace write as
/// <see cref="ItemMetadataRepository"/>, and the state row is replaced in that write.
/// </summary>
/// <param name="connections">Connection factory of the library.</param>
/// <param name="writer">Writer of the library.</param>
internal sealed class AnalyzerStateRepository(LibraryConnectionFactory connections, LibraryWriter writer) : IAnalyzerStateRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<AnalyzerState>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        connections.ReadAsync<IReadOnlyList<AnalyzerState>>(
            async (db, token) => await db.AnalyzerStates.AsNoTracking().Where(state => state.ItemId == itemId)
                .ToListAsync(token).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public Task SetAsync(IReadOnlyCollection<AnalyzerState> states, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Any(state => state is null))
        {
            throw new ArgumentException("A state is missing.", nameof(states));
        }

        if (states.Count == 0)
        {
            return Task.CompletedTask;
        }

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                foreach (var itemId in states.Select(state => state.ItemId).Distinct())
                {
                    await EnsureItemAsync(db, itemId, token).ConfigureAwait(false);
                }

                await ReplaceAsync(db, states, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Id<MediaItem>>> GetItemsAsync(string analyzer, AnalyzerStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzer);

        return connections.ReadAsync<IReadOnlyList<Id<MediaItem>>>(
            async (db, token) => await db.AnalyzerStates.AsNoTracking()
                .Where(state => state.Analyzer == analyzer && state.Status == status)
                .Select(state => state.ItemId)
                .ToListAsync(token).ConfigureAwait(false),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The models table is small, so the current models are matched in memory; each analyzer is then one query on the
    /// runs (or vectors), which reads only the item IDs.
    /// </remarks>
    public Task<OutdatedAnalysis> GetOutdatedAsync(IReadOnlyCollection<AnalyzerProvenance> current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Count == 0)
        {
            return Task.FromResult(OutdatedAnalysis.None);
        }

        return connections.ReadAsync(
            async (db, token) =>
            {
                var models = await db.Models.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
                var byAnalyzer = new Dictionary<string, IReadOnlyList<Id<MediaItem>>>(StringComparer.Ordinal);
                foreach (var provenance in current.GroupBy(provenance => provenance.Analyzer, StringComparer.Ordinal).Select(group => group.Last()))
                {
                    byAnalyzer[provenance.Analyzer] = string.Equals(provenance.Analyzer, AnalyzerKeys.Embedding, StringComparison.Ordinal)
                        ? await OutdatedEmbeddingsAsync(db, CurrentModels(models, provenance), token).ConfigureAwait(false)
                        : await OutdatedRunsAsync(db, provenance, models, token).ConfigureAwait(false);
                }

                return new OutdatedAnalysis(byAnalyzer);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RunApplication> ApplyRunAsync(
        AnalysisRun run,
        IReadOnlyCollection<AiFieldValue> values,
        IReadOnlyCollection<TagAssignment> tags,
        int retainedPreviousRuns,
        AnalyzerState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(state);
        if (state.ItemId != run.ItemId || !string.Equals(state.Analyzer, run.Analyzer, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The state must be run {run.Id}'s item and analyzer.", nameof(state));
        }

        return writer.ExecuteAsync(
            async (db, token) =>
            {
                var metadata = await ItemMetadataRepository.LoadExistingAsync(db, run.ItemId, token).ConfigureAwait(false);
                var application = metadata.ApplyRun(run, values, tags, retainedPreviousRuns);
                await ItemMetadataRepository.ReplaceAsync(db, metadata, token).ConfigureAwait(false);
                await ReplaceAsync(db, [state], token).ConfigureAwait(false);
                return application;
            },
            cancellationToken);
    }

    // The registered models that match the provenance's model; the prompt version is compared on the run.
    private static List<Id<ModelRegistration>> CurrentModels(List<ModelRegistration> models, AnalyzerProvenance provenance) =>
        [.. models.Where(model => provenance.Matches(model, provenance.PromptVersion)).Select(model => model.Id)];

    // Active runs of the analyzer from another model, or with another prompt version.
    private static async Task<IReadOnlyList<Id<MediaItem>>> OutdatedRunsAsync(
        LibraryDbContext db,
        AnalyzerProvenance provenance,
        List<ModelRegistration> models,
        CancellationToken cancellationToken)
    {
        var currentModels = CurrentModels(models, provenance);
        var analyzer = provenance.Analyzer;
        var promptVersion = provenance.PromptVersion;
        return await db.AnalysisRuns.AsNoTracking()
            .Where(run => run.Analyzer == analyzer && run.IsActive)
            .Where(run => !currentModels.Contains(run.ModelId) || run.PromptVersion != promptVersion)
            .Where(run => db.MediaItems.Any(item => item.Id == run.ItemId && item.Status != MediaItemStatus.Missing))
            .Select(run => run.ItemId)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // Items with vectors, none of them from a current model.
    private static async Task<IReadOnlyList<Id<MediaItem>>> OutdatedEmbeddingsAsync(
        LibraryDbContext db,
        List<Id<ModelRegistration>> currentModels,
        CancellationToken cancellationToken) =>
        await db.Embeddings.AsNoTracking()
            .Where(embedding => !currentModels.Contains(embedding.ModelId))
            .Select(embedding => embedding.ItemId)
            .Distinct()
            .Where(itemId => !db.Embeddings.Any(embedding => embedding.ItemId == itemId && currentModels.Contains(embedding.ModelId)))
            .Where(itemId => db.MediaItems.Any(item => item.Id == itemId && item.Status != MediaItemStatus.Missing))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static async Task EnsureItemAsync(LibraryDbContext db, Id<MediaItem> itemId, CancellationToken cancellationToken)
    {
        if (!await db.MediaItems.AnyAsync(item => item.Id == itemId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException($"Media item {itemId} doesn't exist.");
        }
    }

    private static async Task ReplaceAsync(LibraryDbContext db, IReadOnlyCollection<AnalyzerState> states, CancellationToken cancellationToken)
    {
        foreach (var state in states)
        {
            await db.AnalyzerStates.Where(existing => existing.ItemId == state.ItemId && existing.Analyzer == state.Analyzer)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        // The last state of an item and analyzer wins.
        db.AnalyzerStates.AddRange(states.GroupBy(state => (state.ItemId, state.Analyzer)).Select(group => group.Last()));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }
}
