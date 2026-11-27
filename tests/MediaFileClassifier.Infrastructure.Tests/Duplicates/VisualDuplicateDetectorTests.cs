using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Media;
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
        using var detector = new VisualDuplicateDetector(bed.Library.Clock, new NotReadyVectors());

        Assert.Null(await detector.ReconcileAsync(bed.Library.Library, VisualDuplicateDetector.BatchSize, Token));
        Assert.Equal(2, await bed.Library.Library.VisualDuplicates.CountQueuedAsync(Token));
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

    private Task SetSensitivityAsync(VisualDuplicateSensitivity sensitivity) =>
        bed.Library.Library.UpdateSettingsAsync(settings => settings with { VisualDuplicateSensitivity = sensitivity }, Token);

    private sealed class NotReadyVectors : Abstractions.Search.IItemVectorSimilarity
    {
        public bool IsReady(Abstractions.Libraries.IOpenLibrary library) => false;

        public IReadOnlyList<Abstractions.Search.ItemSimilarity>? FindNearest(Abstractions.Libraries.IOpenLibrary library, Domain.Identity.Id<Domain.Analysis.ModelRegistration> modelId, Domain.Identity.Id<MediaItem> itemId, int count, float minSimilarity) => null;

        public float? GetSimilarity(Abstractions.Libraries.IOpenLibrary library, Domain.Identity.Id<Domain.Analysis.ModelRegistration> modelId, Domain.Identity.Id<MediaItem> first, Domain.Identity.Id<MediaItem> second) => null;
    }
}
