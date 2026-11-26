using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.UseCases.Duplicates.Visual;

/// <summary>
/// Finds visual near-duplicate groups for the queued items of a library (E12-S02). A group is a connected set of
/// items that match by <see cref="VisualMatchRules"/>; exact copies (same content hash) count as one item, shown by
/// the copy with the lowest ID, so they're never reported again as visual duplicates of each other. A check starts
/// from the queued items and the members of their visual groups, follows matches until no new item is reached, and
/// replaces the groups within what it reached. Because a group is a whole connected set, checking items one batch at
/// a time gives the same groups as checking all of them at once (E12-S02-T04).
/// </summary>
/// <remarks>
/// The candidates of the open library are kept in memory between checks, and only queued items are read again: every
/// change that matters queues the item. Checks run one at a time.
/// </remarks>
/// <param name="clock">The clock.</param>
/// <param name="vectors">Embedding similarity from the in-memory vector index; without it, no embedding confirms a match.</param>
public sealed class VisualDuplicateDetector(TimeProvider clock, IItemVectorSimilarity? vectors = null) : IDisposable
{
    /// <summary>
    /// Queued items checked per call, so one write never takes long (D072).
    /// </summary>
    public const int BatchSize = 200;

    // Most items an embedding search returns per item at the loose sensitivity.
    private const int NeighbourCount = 16;

    private readonly SemaphoreSlim gate = new(1, 1);
    private Snapshot? snapshot;

    /// <summary>
    /// Checks up to <paramref name="limit"/> queued items and writes the visual groups found.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="limit">Most queued items to take.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed; <see cref="VisualReconcileResult.Items"/> is 0 when the queue was empty. <c>null</c> when
    /// the vector index isn't ready yet, so the queue is left for later.</returns>
    public async Task<VisualReconcileResult?> ReconcileAsync(IOpenLibrary library, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var repository = library.VisualDuplicates;
            var batch = await repository.PeekQueueAsync(limit, cancellationToken).ConfigureAwait(false);
            if (batch.IsEmpty)
            {
                return new VisualReconcileResult(0, 0, 0, 0, 0);
            }

            var model = vectors is null ? null : await PickModelAsync(library, cancellationToken).ConfigureAwait(false);
            if (model is not null && !vectors!.IsReady(library))
            {
                return null;
            }

            var current = await RefreshAsync(library, batch.Items, cancellationToken).ConfigureAwait(false);
            var rules = VisualMatchRules.Strict;
            var check = new Check(current, rules, model is { } id ? (a, b) => vectors!.GetSimilarity(library, id, a, b) ?? float.NaN : (_, _) => float.NaN);
            if (model is { } modelId && rules.UsesEmbeddingCandidates)
            {
                check.Nearest = itemId => vectors!.FindNearest(library, modelId, itemId, NeighbourCount, rules.EmbeddingSimilarity) ?? [];
            }

            var groups = await check.RunAsync(batch.Items, repository, cancellationToken).ConfigureAwait(false);
            return await repository.ApplyAsync(new VisualGroupUpdate(check.Scope, groups, batch), clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    // The library's search model (D051), else the model with stored vectors; matching compares only its vectors.
    private static async Task<Id<ModelRegistration>?> PickModelAsync(IOpenLibrary library, CancellationToken cancellationToken)
    {
        if (library.Library.Settings.ActiveSearchModelId is { } active)
        {
            return active;
        }

        var models = await library.Embeddings.GetModelsInUseAsync(cancellationToken).ConfigureAwait(false);
        return models.Count == 0 ? null : models.OrderBy(model => model.Value).First();
    }

    private async Task<Snapshot> RefreshAsync(IOpenLibrary library, IReadOnlyList<Id<MediaItem>> changed, CancellationToken cancellationToken)
    {
        if (snapshot is { } kept && ReferenceEquals(kept.Library, library))
        {
            var fresh = await library.VisualDuplicates.GetCandidatesAsync(changed, cancellationToken).ConfigureAwait(false);
            kept.Replace(changed, fresh);
            return kept;
        }

        // The whole set is read when a library is first checked; a check that's cancelled meanwhile reads it again.
        snapshot = null;
        var all = await library.VisualDuplicates.GetCandidatesAsync(null, cancellationToken).ConfigureAwait(false);
        snapshot = new Snapshot(library, all);
        return snapshot;
    }

    /// <summary>
    /// The candidates of one library, with the perceptual hash indexes built from them.
    /// </summary>
    private sealed class Snapshot
    {
        private readonly Dictionary<Id<MediaItem>, VisualCandidate> items = [];
        private Indexes? indexes;

        public Snapshot(IOpenLibrary library, IReadOnlyList<VisualCandidate> candidates)
        {
            Library = library;
            foreach (var candidate in candidates)
            {
                items[candidate.ItemId] = candidate;
            }
        }

        public IOpenLibrary Library { get; }

        public Indexes Current => indexes ??= new Indexes(items);

        public void Replace(IReadOnlyList<Id<MediaItem>> changed, IReadOnlyList<VisualCandidate> fresh)
        {
            foreach (var itemId in changed)
            {
                items.Remove(itemId);
            }

            foreach (var candidate in fresh)
            {
                items[candidate.ItemId] = candidate;
            }

            indexes = null;
        }
    }

    /// <summary>
    /// The candidates by item and by content hash, and perceptual hash indexes of the items that stand for their
    /// exact copies.
    /// </summary>
    private sealed class Indexes
    {
        private readonly Dictionary<UInt128, Id<MediaItem>> representatives = [];

        public Indexes(Dictionary<Id<MediaItem>, VisualCandidate> items)
        {
            Items = items;
            foreach (var candidate in items.Values)
            {
                if (candidate.ContentHash is { } hash
                    && (!representatives.TryGetValue(hash, out var kept) || candidate.ItemId.Value.CompareTo(kept.Value) < 0))
                {
                    representatives[hash] = candidate.ItemId;
                }
            }

            foreach (var candidate in items.Values.Where(candidate => Representative(candidate.ItemId) == candidate.ItemId))
            {
                if (candidate.MediaType == MediaType.Image)
                {
                    if (candidate.PerceptualHash is { } hash)
                    {
                        Stills.Add(candidate.ItemId, hash);
                    }
                }
                else
                {
                    foreach (var hash in HashesOf(candidate))
                    {
                        Moving.Add(candidate.ItemId, hash);
                    }
                }
            }
        }

        public Dictionary<Id<MediaItem>, VisualCandidate> Items { get; }

        public PerceptualHashIndex<Id<MediaItem>> Stills { get; } = new();

        public PerceptualHashIndex<Id<MediaItem>> Moving { get; } = new();

        public static IEnumerable<ulong> HashesOf(VisualCandidate candidate) =>
            candidate.FrameHashes.Count > 0 ? candidate.FrameHashes : candidate.PerceptualHash is { } hash ? [hash] : [];

        // The copy with the lowest ID stands for all exact copies; an item that isn't a candidate stands for itself.
        public Id<MediaItem> Representative(Id<MediaItem> itemId) =>
            Items.TryGetValue(itemId, out var candidate) && candidate.ContentHash is { } hash ? representatives[hash] : itemId;
    }

    /// <summary>
    /// One check: follows matches from the queued items and finds the connected groups among what it reached.
    /// </summary>
    private sealed class Check(Snapshot snapshot, VisualMatchRules rules, Func<Id<MediaItem>, Id<MediaItem>, float> similarity)
    {
        private readonly Indexes indexes = snapshot.Current;
        private readonly Dictionary<Id<MediaItem>, Id<MediaItem>> parents = [];
        private readonly HashSet<Id<MediaItem>> visited = [];

        public Func<Id<MediaItem>, IReadOnlyList<ItemSimilarity>>? Nearest { get; set; }

        public HashSet<Id<MediaItem>> Scope { get; } = [];

        public async Task<IReadOnlyList<IReadOnlyCollection<Id<MediaItem>>>> RunAsync(
            IReadOnlyList<Id<MediaItem>> queued,
            IVisualDuplicateRepository repository,
            CancellationToken cancellationToken)
        {
            var frontier = new List<Id<MediaItem>>();
            await ReachAsync(queued, frontier, repository, cancellationToken).ConfigureAwait(false);
            while (frontier.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reached = new List<Id<MediaItem>>();
                foreach (var node in frontier)
                {
                    foreach (var other in Neighbours(node))
                    {
                        Union(node, other);
                        if (!visited.Contains(other))
                        {
                            reached.Add(other);
                        }
                    }
                }

                frontier.Clear();
                await ReachAsync(reached, frontier, repository, cancellationToken).ConfigureAwait(false);
            }

            return [.. parents.Keys
                .GroupBy(Find)
                .Where(group => group.Count() >= 2)
                .Select(group => (IReadOnlyCollection<Id<MediaItem>>)[.. group.OrderBy(itemId => itemId.Value)])];
        }

        // Adds items to the scope with their visual groups' members, and queues the nodes not seen yet.
        private async Task ReachAsync(IReadOnlyList<Id<MediaItem>> items, List<Id<MediaItem>> frontier, IVisualDuplicateRepository repository, CancellationToken cancellationToken)
        {
            if (items.Count == 0)
            {
                return;
            }

            var groups = await repository.GetGroupsOfAsync(items, cancellationToken).ConfigureAwait(false);
            foreach (var itemId in items.Concat(groups.SelectMany(group => group)))
            {
                Scope.Add(itemId);
                if (!indexes.Items.ContainsKey(itemId))
                {
                    continue;
                }

                var node = indexes.Representative(itemId);
                Scope.Add(node);
                parents.TryAdd(node, node);
                if (visited.Add(node))
                {
                    frontier.Add(node);
                }
            }
        }

        private IEnumerable<Id<MediaItem>> Neighbours(Id<MediaItem> node)
        {
            var candidate = indexes.Items[node];
            var found = new HashSet<Id<MediaItem>>();
            if (candidate.MediaType == MediaType.Image)
            {
                if (candidate.PerceptualHash is { } hash)
                {
                    found.UnionWith(indexes.Stills.FindWithin(hash, rules.CandidateHashDistance).Select(match => match.Key));
                }
            }
            else
            {
                foreach (var hash in Indexes.HashesOf(candidate))
                {
                    found.UnionWith(indexes.Moving.FindWithin(hash, rules.CandidateHashDistance).Select(match => match.Key));
                }
            }

            if (Nearest is { } nearest)
            {
                found.UnionWith(nearest(node)
                    .Where(hit => indexes.Items.ContainsKey(hit.ItemId))
                    .Select(hit => indexes.Representative(hit.ItemId)));
            }

            found.Remove(node);
            return found.Where(other => rules.Matches(candidate, indexes.Items[other], similarity(node, other)));
        }

        private Id<MediaItem> Find(Id<MediaItem> node)
        {
            while (parents[node] != node)
            {
                var parent = parents[node];
                parents[node] = parents[parent];
                node = parent;
            }

            return node;
        }

        private void Union(Id<MediaItem> first, Id<MediaItem> second)
        {
            parents.TryAdd(second, second);
            var (a, b) = (Find(first), Find(second));
            if (a != b)
            {
                parents[a.Value.CompareTo(b.Value) < 0 ? b : a] = a.Value.CompareTo(b.Value) < 0 ? a : b;
            }
        }
    }
}
