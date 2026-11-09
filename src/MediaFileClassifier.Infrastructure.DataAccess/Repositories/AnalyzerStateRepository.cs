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
