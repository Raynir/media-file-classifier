using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Duplicates.Visual;

namespace MediaFileClassifier.Infrastructure.Tests.Duplicates;

/// <summary>
/// Visual near-duplicate detection on the test set of real-photo features (E12-S02), with a real library and vector
/// index.
/// </summary>
public sealed class VisualDuplicateDetectorTests : IAsyncLifetime
{
    private static readonly IReadOnlyList<NearDuplicateFeature> Features = NearDuplicateFeature.Load();

    private VisualDuplicateTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await VisualDuplicateTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task TestSet_Strict_GivesTheExpectedGroups()
    {
        await bed.AddAsync(Features.Where(feature => feature.MediaType == "Image"));

        await bed.ReconcileAllAsync();

        Assert.Equal(VisualDuplicateTestBed.Expected(Features.Where(feature => feature.MediaType == "Image"), loose: false), await bed.GroupsAsync());
        Assert.Equal(0, await bed.Library.Library.VisualDuplicates.CountQueuedAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestSet_EachSensitivity_GivesItsExpectedGroups(bool loose)
    {
        await SetSensitivityAsync(loose ? VisualDuplicateSensitivity.Loose : VisualDuplicateSensitivity.Strict);
        await bed.AddAsync(Features);

        await bed.ReconcileAllAsync();

        Assert.Equal(VisualDuplicateTestBed.Expected(Features, loose), await bed.GroupsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BurstShots_OnlyLooseGroupsThem(bool loose)
    {
        await SetSensitivityAsync(loose ? VisualDuplicateSensitivity.Loose : VisualDuplicateSensitivity.Strict);
        await bed.AddAsync(Features.Where(feature => feature.Name.StartsWith("football-burst-", StringComparison.Ordinal)));

        await bed.ReconcileAllAsync();

        Assert.Equal(loose ? ["football-burst-1, football-burst-2, football-burst-3, football-burst-4"] : [], await bed.GroupsAsync());
    }

    [Fact]
    public async Task SensitivityChange_FullPass_RegroupsEveryItem()
    {
        var images = Features.Where(feature => feature.MediaType == "Image").ToList();
        await bed.AddAsync(images);
        await bed.ReconcileAllAsync();
        var strictGroups = await bed.Library.Library.Duplicates.GetGroupsAsync(null, 0, 1000, Token);

        await SetSensitivityAsync(VisualDuplicateSensitivity.Loose);
        await bed.Library.Library.VisualDuplicates.QueueAllAsync(Token);
        await bed.ReconcileAllAsync();
        Assert.Equal(VisualDuplicateTestBed.Expected(images, loose: true), await bed.GroupsAsync());

        await SetSensitivityAsync(VisualDuplicateSensitivity.Strict);
        await bed.Library.Library.VisualDuplicates.QueueAllAsync(Token);
        await bed.ReconcileAllAsync();
        Assert.Equal(VisualDuplicateTestBed.Expected(images, loose: false), await bed.GroupsAsync());

        // The groups both sensitivities find keep their IDs, so a review state survives the round trip (D578).
        var groups = await bed.Library.Library.Duplicates.GetGroupsAsync(null, 0, 1000, Token);
        Assert.Equal(strictGroups.Select(group => group.Id.Value).Order(), groups.Select(group => group.Id.Value).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalAndFullPass_GiveTheSameGroups(bool loose)
    {
        var sensitivity = loose ? VisualDuplicateSensitivity.Loose : VisualDuplicateSensitivity.Strict;

        // Incremental: items arrive a few at a time, in an order that splits most groups across batches, and the
        // queue is worked off after each batch, as the maintainer does after item changes (E12-S02-T04).
        await SetSensitivityAsync(sensitivity);
        var scattered = Features.Select((feature, index) => (feature, index)).OrderBy(entry => entry.index % 7).ThenBy(entry => entry.index).Select(entry => entry.feature).ToList();
        foreach (var batch in scattered.Chunk(5))
        {
            await bed.AddAsync(batch);
            await bed.ReconcileAllAsync();
        }

        var incremental = await bed.GroupsAsync();

        // Full pass: a second library gets every item with nothing queued, then queues all and works the queue off.
        await using var full = await VisualDuplicateTestBed.CreateAsync();
        await full.Library.Library.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = sensitivity }, Token);
        await full.AddAsync(Features);
        await DropQueueAsync(full);
        Assert.True(await full.Library.Library.VisualDuplicates.QueueAllAsync(Token) >= Features.Count);
        await full.ReconcileAllAsync();

        Assert.Equal(VisualDuplicateTestBed.Expected(Features, loose), incremental);
        Assert.Equal(incremental, await full.GroupsAsync());
    }

    [Fact]
    public async Task Videos_ReencodedCopyIsGroupedWithTheOriginal()
    {
        await bed.AddAsync(Features.Where(feature => feature.MediaType == "Video"));

        await bed.ReconcileAllAsync();

        Assert.Equal(["video-short, video-short-reencoded"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task AnimatedImages_ConvertedCopyIsGroupedWithTheOriginal()
    {
        await bed.AddAsync(Features.Where(feature => feature.MediaType == "AnimatedImage"));

        await bed.ReconcileAllAsync();

        Assert.Equal(["gif-animated, gif-animated-webp"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task VideoDurationsTooFarApart_AreNotGrouped()
    {
        var original = Features.Single(feature => feature.Name == "video-short");
        await bed.AddAsync([original, original with { Name = "video-short-longer", Content = "longer", DurationMs = original.DurationMs + 600 }]);

        await bed.ReconcileAllAsync();

        Assert.Empty(await bed.GroupsAsync());
    }

    [Fact]
    public async Task StillAndAnimation_AreNeverGrouped()
    {
        var animation = Features.Single(feature => feature.Name == "gif-animated");
        await bed.AddAsync([animation, animation with { Name = "gif-still", MediaType = "Image", Content = "still", FrameHashes = [], DurationMs = null }]);

        await bed.ReconcileAllAsync();

        Assert.Empty(await bed.GroupsAsync());
    }

    [Fact]
    public async Task ExactCopies_AreNeverVisualDuplicatesOfEachOther()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "cat" or "cat-copy"));

        await bed.ReconcileAllAsync();

        Assert.Empty(await bed.GroupsAsync());
    }

    [Fact]
    public async Task ExactCopies_TheOldestStandsForThemInTheVisualGroup()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "cat" or "cat-copy" or "cat-q30"));

        await bed.ReconcileAllAsync();

        Assert.Equal(["cat, cat-q30"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task ExactCopyStandingInGoesMissing_TheOtherCopyTakesItsPlace()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "cat" or "cat-copy" or "cat-q30"));
        await bed.ReconcileAllAsync();

        var cat = bed.Items["cat"];
        cat.ChangeStatus(MediaItemStatus.Missing, bed.Library.Clock.Tick());
        await bed.Library.Library.Items.UpdateAsync(cat, Token);
        await bed.ReconcileAllAsync();

        Assert.Equal(["cat-copy, cat-q30"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task IgnoredGroup_GainsAMember_IsOpenAgain()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "plaque" or "plaque-q30"));
        await bed.ReconcileAllAsync();
        var group = Assert.Single(await bed.Library.Library.Duplicates.GetGroupsAsync(null, 0, 10, Token));
        await bed.Library.Library.Duplicates.ChangeStateAsync(group.Id, DuplicateGroupState.Ignored, bed.Library.Clock.Tick(), Token);

        await bed.AddAsync(Features.Where(feature => feature.Name is "plaque-thumb"));
        var result = await bed.ReconcileAllAsync();

        Assert.Equal(1, result.Reopened);
        Assert.Equal(DuplicateGroupState.Open, (await bed.Library.Library.Duplicates.GetAsync(group.Id, Token))!.State);
        Assert.Equal(["plaque, plaque-q30, plaque-thumb"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task MemberDeleted_GroupOfTwoIsDeleted()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "plaque" or "plaque-q30"));
        await bed.ReconcileAllAsync();

        await bed.Library.Library.Items.DeleteAsync(bed.Items["plaque-q30"].Id, Token);
        await bed.ReconcileAllAsync();

        Assert.Empty(await bed.GroupsAsync());
    }

    [Fact]
    public async Task VectorIndexNotReady_LeavesTheQueue()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "plaque" or "plaque-q30"));
        using var detector = new VisualDuplicateDetector(bed.Library.Clock, new NotReadyVectors(VectorIndexState.Loading));

        Assert.Null(await detector.ReconcileAsync(bed.Library.Library, VisualDuplicateDetector.BatchSize, Token));
        Assert.Equal(2, await bed.Library.Library.VisualDuplicates.CountQueuedAsync(Token));
    }

    [Fact]
    public async Task VectorIndexFailed_ChecksWithoutEmbeddings()
    {
        await bed.AddAsync(Features.Where(feature => feature.Name is "plaque" or "plaque-q30"));
        using var detector = new VisualDuplicateDetector(bed.Library.Clock, new NotReadyVectors(VectorIndexState.Failed));

        while (await detector.ReconcileAsync(bed.Library.Library, VisualDuplicateDetector.BatchSize, Token) is { Items: > 0 })
        {
        }

        Assert.Equal(["plaque, plaque-q30"], await bed.GroupsAsync());
        Assert.Equal(0, await bed.Library.Library.VisualDuplicates.CountQueuedAsync(Token));
    }

    [Fact]
    public async Task Loose_NearestVectorsCrowdedByOtherKinds_StillFindsTheMatch()
    {
        // Twenty videos share the second burst shot's vector exactly, so they're its nearest vectors; they can't match
        // a still. The shot's hash is inverted, so only the embedding tier (cosine 0.964) links it to the first shot.
        await SetSensitivityAsync(VisualDuplicateSensitivity.Loose);
        var first = Features.Single(feature => feature.Name == "football-burst-1");
        var second = Features.Single(feature => feature.Name == "football-burst-2");
        var inverted = (~NearDuplicateFeature.ParseHash(second.PerceptualHash!)).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        await bed.AddAsync([first, .. Enumerable.Range(0, 20).Select(i => second with
        {
            Name = $"crowd-{i}",
            MediaType = "Video",
            Content = $"crowd-{i}",
            PerceptualHash = null,
            FrameHashes = [inverted],
            DurationMs = 1000,
        })]);
        await bed.ReconcileAllAsync();

        await bed.AddAsync([second with { PerceptualHash = inverted }]);
        await bed.ReconcileAllAsync();

        Assert.Contains("football-burst-1, football-burst-2", await bed.GroupsAsync());
    }

    [Fact]
    public async Task AnimationWithOnlyItsFirstFrameHash_MatchesAVideoOfIt()
    {
        var video = Features.Single(feature => feature.Name == "video-short");
        await bed.AddAsync([video, video with { Name = "video-short-gif", MediaType = "AnimatedImage", Content = "gif", PerceptualHash = video.FrameHashes[0], FrameHashes = [] }]);

        await bed.ReconcileAllAsync();

        Assert.Equal(["video-short, video-short-gif"], await bed.GroupsAsync());
    }

    [Fact]
    public async Task WithoutEmbeddings_StrictGroupsByPerceptualHashAlone()
    {
        using var detector = new VisualDuplicateDetector(bed.Library.Clock);
        await bed.AddAsync(Features.Where(feature => feature.Name is "cat" or "cat-thumb" or "clock" or "clock-crop4"));

        while (await detector.ReconcileAsync(bed.Library.Library, VisualDuplicateDetector.BatchSize, Token) is { Items: > 0 })
        {
        }

        Assert.Equal(["cat, cat-thumb"], await bed.GroupsAsync());
    }

    private static async Task DropQueueAsync(VisualDuplicateTestBed target)
    {
        var queued = await target.Library.Library.VisualDuplicates.PeekQueueAsync(int.MaxValue, Token);
        await target.Library.Library.VisualDuplicates.ApplyAsync(new([], [], queued), target.Library.Clock.Tick(), Token);
        Assert.Equal(0, await target.Library.Library.VisualDuplicates.CountQueuedAsync(Token));
    }

    private Task SetSensitivityAsync(VisualDuplicateSensitivity sensitivity) =>
        bed.Library.Library.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = sensitivity }, Token);

    private sealed class NotReadyVectors(VectorIndexState state) : IItemVectorSimilarity
    {
        public VectorIndexState GetState(Abstractions.Libraries.IOpenLibrary library) => state;

        public IReadOnlyList<ItemSimilarity>? FindNearest(Abstractions.Libraries.IOpenLibrary library, Domain.Identity.Id<Domain.Analysis.ModelRegistration> modelId, Domain.Identity.Id<MediaItem> itemId, int count, float minSimilarity) => null;

        public float? GetSimilarity(Abstractions.Libraries.IOpenLibrary library, Domain.Identity.Id<Domain.Analysis.ModelRegistration> modelId, Domain.Identity.Id<MediaItem> first, Domain.Identity.Id<MediaItem> second) => null;
    }
}
