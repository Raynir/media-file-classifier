using System.IO.Hashing;
using System.Text;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.UseCases.Duplicates.Visual;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// A real library and search index loaded with the near-duplicate test set (<see cref="NearDuplicateFeature"/>), and
/// a <see cref="VisualDuplicateDetector"/> on them (E12-S02).
/// </summary>
internal sealed class VisualDuplicateTestBed : IAsyncDisposable
{
    private readonly Dictionary<Id<MediaItem>, string> names = [];

    private VisualDuplicateTestBed(LibraryTestBed library, SearchIndex index, ModelRegistration model)
    {
        Library = library;
        Index = index;
        Model = model;
        Detector = new VisualDuplicateDetector(library.Clock, index);
    }

    public LibraryTestBed Library { get; }

    public SearchIndex Index { get; }

    public ModelRegistration Model { get; }

    public VisualDuplicateDetector Detector { get; }

    public Dictionary<string, MediaItem> Items { get; } = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<VisualDuplicateTestBed> CreateAsync()
    {
        var library = await LibraryTestBed.CreateAsync();
        var model = ModelRegistration.Create("local", "siglip2-base-patch16-256", "1.0", null, 768, LibraryTestBed.Start);
        await library.Library.Models.AddAsync(model, Token);
        var index = new SearchIndex(new TestSession(library.Library), NullLogger<SearchIndex>.Instance);
        await index.Ready;
        return new VisualDuplicateTestBed(library, index, model);
    }

    /// <summary>
    /// Adds items of the test set, in the given order.
    /// </summary>
    /// <param name="features">The items.</param>
    /// <returns>A task.</returns>
    public async Task AddAsync(IEnumerable<NearDuplicateFeature> features)
    {
        foreach (var feature in features)
        {
            var mediaType = Enum.Parse<MediaType>(feature.MediaType);
            var item = MediaItem.Discover(
                Library.Folder.Id,
                RelativePath.From($"set/{feature.Name}.{(mediaType == MediaType.Video ? "mp4" : "jpg")}"),
                mediaType,
                1000 + Items.Count,
                LibraryTestBed.Start,
                LibraryTestBed.Start,
                (UInt128)(Items.Count + 1),
                LibraryTestBed.Start);
            item.SetHashes(item.Hashes with
            {
                ContentHash = XxHash128.HashToUInt128(Encoding.UTF8.GetBytes(feature.Content)),
                PerceptualHash = feature.PerceptualHash is { } hash ? NearDuplicateFeature.ParseHash(hash) : null,
            });
            await Library.Library.Items.AddAsync(item, Token);
            if (feature.DurationMs is { } duration)
            {
                await Library.Library.Items.UpdateAsync(item, new TechnicalMetadata { ItemId = item.Id, Duration = TimeSpan.FromMilliseconds(duration), ExtractedAt = LibraryTestBed.Start }, Token);
            }

            if (feature.FrameHashes.Count > 0)
            {
                await Library.Library.Keyframes.ReplaceAsync(
                    item.Id,
                    [.. feature.FrameHashes.Select((hash, index) => new Keyframe(Id<Keyframe>.New(), item.Id, index, TimeSpan.FromMilliseconds(index * 100), null, NearDuplicateFeature.ParseHash(hash)))],
                    Token);
            }

            await Library.Library.Embeddings.ReplaceAsync(
                item.Id,
                Model.Id,
                [Embedding.Create(item.Id, Model, EmbeddingKind.Image, feature.DecodeVector(), Library.Clock.Tick())],
                Token);
            Items[feature.Name] = item;
            names[item.Id] = feature.Name;
        }
    }

    /// <summary>
    /// Checks the queue until it's empty.
    /// </summary>
    /// <returns>What changed, summed over the batches.</returns>
    public async Task<VisualReconcileResult> ReconcileAllAsync()
    {
        int items = 0, created = 0, changed = 0, reopened = 0, deleted = 0;
        while (await Detector.ReconcileAsync(Library.Library, VisualDuplicateDetector.BatchSize, Token) is { Items: > 0 } result)
        {
            (items, created, changed, reopened, deleted) = (items + result.Items, created + result.Created, changed + result.Changed, reopened + result.Reopened, deleted + result.Deleted);
        }

        return new VisualReconcileResult(items, created, changed, reopened, deleted);
    }

    /// <summary>
    /// Gets the visual groups as sorted lists of item names, sorted by their first name.
    /// </summary>
    /// <returns>The groups.</returns>
    public async Task<List<string>> GroupsAsync()
    {
        var groups = new List<string>();
        foreach (var group in await Library.Library.Duplicates.GetGroupsAsync(null, 0, 1000, Token))
        {
            if (group.Type == DuplicateGroupType.Visual)
            {
                var members = await Library.Library.Duplicates.GetMembersAsync(group.Id, Token);
                groups.Add(string.Join(", ", members.Select(member => names[member.ItemId]).Order(StringComparer.Ordinal)));
            }
        }

        groups.Sort(StringComparer.Ordinal);
        return groups;
    }

    /// <summary>
    /// Gets the groups a sensitivity is expected to find, in the shape of <see cref="GroupsAsync"/>.
    /// </summary>
    /// <param name="features">The items added.</param>
    /// <param name="loose">Whether the loose groups are expected.</param>
    /// <returns>The groups.</returns>
    public static List<string> Expected(IEnumerable<NearDuplicateFeature> features, bool loose)
    {
        var groups = features
            .Where(feature => (loose ? feature.Loose : feature.Strict) is not null)
            .GroupBy(feature => loose ? feature.Loose : feature.Strict)
            .Select(group => string.Join(", ", group.Select(feature => feature.Name).Order(StringComparer.Ordinal)))
            .ToList();
        groups.Sort(StringComparer.Ordinal);
        return groups;
    }

    public async ValueTask DisposeAsync()
    {
        Detector.Dispose();
        Index.Dispose();
        await Library.DisposeAsync();
    }
}
