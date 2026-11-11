using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.Embeddings;

/// <summary>
/// Tests for <see cref="ItemEmbeddingAnalyzer"/> (E06-S03-T01) against a real library, with a fake embedding model.
/// </summary>
public sealed class ItemEmbeddingAnalyzerTests : IAsyncLifetime
{
    private readonly FakeEmbeddingProvider provider = new(dimension: 8);
    private readonly StubFrameSampler sampler = new();
    private readonly UsageStore store = new();
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Analyze_StillImage_StoresOneItemVector()
    {
        var item = await AddItemAsync(1);

        var result = await CreateAnalyzer().AnalyzeAsync(bed.Library, Content(item, SampleMedia.JpegWithExif), Format(MediaType.Image), Metadata(item), Token);

        var stored = Assert.Single(await bed.Library.Embeddings.GetAsync(item.Id, Token));
        Assert.Null(stored.KeyframeId);
        Assert.Equal(result.Model!.Id, stored.ModelId);
        Assert.True(provider.Model.Matches(result.Model));
        Assert.Equal(1.0, Norm(stored.Vector.Span), 3);
        Assert.Empty(sampler.Calls);
    }

    [Fact]
    public async Task Analyze_Video_StoresKeyframesTheirVectorsAndTheMean()
    {
        var item = await AddItemAsync(1);
        sampler.Frames = 3;

        var result = await CreateAnalyzer().AnalyzeAsync(bed.Library, Content(item, SampleMedia.ActionVideo), Format(MediaType.Video), Metadata(item), Token);

        var keyframes = await bed.Library.Keyframes.GetForItemAsync(item.Id, Token);
        var stored = await bed.Library.Embeddings.GetAsync(item.Id, Token);
        Assert.Equal(3, result.KeyframeCount);
        Assert.Equal(3, keyframes.Count);
        Assert.Equal(4, stored.Count);
        var frameVectors = keyframes.Select(keyframe => stored.Single(embedding => embedding.KeyframeId == keyframe.Id).Vector).ToList();
        var itemVector = stored.Single(embedding => embedding.KeyframeId is null).Vector.ToArray();
        var mean = KeyframeAggregation.MeanEmbedding(frameVectors);
        Assert.Equal(mean.Length, itemVector.Length);
        Assert.All(mean.Zip(itemVector), pair => Assert.Equal(pair.First, pair.Second, 0.002f));
    }

    [Fact]
    public async Task Analyze_AgainWithFewerFrames_ReplacesTheOldVectors()
    {
        var item = await AddItemAsync(1);
        var analyzer = CreateAnalyzer();
        sampler.Frames = 4;
        await analyzer.AnalyzeAsync(bed.Library, Content(item, SampleMedia.ActionVideo), Format(MediaType.AnimatedImage), Metadata(item), Token);

        sampler.Frames = 2;
        await analyzer.AnalyzeAsync(bed.Library, Content(item, SampleMedia.ActionVideo), Format(MediaType.AnimatedImage), Metadata(item), Token);

        Assert.Equal(3, (await bed.Library.Embeddings.GetAsync(item.Id, Token)).Count);
    }

    [Fact]
    public async Task Analyze_SamplingFailed_StoresNothingAndReturnsTheWarning()
    {
        var item = await AddItemAsync(1);
        sampler.Frames = 0;

        var result = await CreateAnalyzer().AnalyzeAsync(bed.Library, Content(item, SampleMedia.ActionVideo), Format(MediaType.Video), Metadata(item), Token);

        Assert.Null(result.Model);
        Assert.Equal(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable, Assert.Single(result.Warnings).Code);
        Assert.Empty(await bed.Library.Embeddings.GetAsync(item.Id, Token));
    }

    [Fact]
    public async Task Analyze_NoEmbeddingProvider_ThrowsModelMissingBeforeSampling()
    {
        var item = await AddItemAsync(1);
        var analyzer = new ItemEmbeddingAnalyzer(new AiProviderRegistry([], bed.Clock), sampler, store, bed.Clock);

        var error = await Assert.ThrowsAsync<AiProviderException>(
            () => analyzer.AnalyzeAsync(bed.Library, Content(item, SampleMedia.ActionVideo), Format(MediaType.Video), Metadata(item), Token));

        Assert.Equal(AiFailureKind.ModelMissing, error.Kind);
        Assert.Empty(sampler.Calls);
    }

    [Fact]
    public async Task Analyze_TwoItems_RegistersTheModelOnce()
    {
        var analyzer = CreateAnalyzer();
        var first = await AddItemAsync(1);
        var second = await AddItemAsync(2);

        var a = await analyzer.AnalyzeAsync(bed.Library, Content(first, SampleMedia.WebP), Format(MediaType.Image), Metadata(first), Token);
        var b = await analyzer.AnalyzeAsync(bed.Library, Content(second, SampleMedia.PngWithAlpha), Format(MediaType.Image), Metadata(second), Token);

        Assert.Equal(a.Model!.Id, b.Model!.Id);
        Assert.Single(await bed.Library.Models.GetAllAsync(Token), model => provider.Model.Matches(model));
    }

    private static double Norm(ReadOnlySpan<float> vector)
    {
        var sum = 0.0;
        foreach (var value in vector)
        {
            sum += value * (double)value;
        }

        return Math.Sqrt(sum);
    }

    private static MediaContent Content(MediaItem item, string path) =>
        new(item.Id, DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(File.OpenRead(path))) { LocalPath = path };

    private static MediaFormatDetection Format(MediaType type) => new() { MediaType = type };

    private static TechnicalMetadata Metadata(MediaItem item) => new() { ItemId = item.Id, ExtractedAt = DateTimeOffset.UnixEpoch };

    private ItemEmbeddingAnalyzer CreateAnalyzer() =>
        new(new AiProviderRegistry([provider], bed.Clock), sampler, store, bed.Clock);

    private async Task<MediaItem> AddItemAsync(int number)
    {
        var item = bed.NewItem(number);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private sealed class StubFrameSampler : IFrameSampler
    {
        public int Frames { get; set; } = 2;

        public List<Id<MediaItem>> Calls { get; } = [];

        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken)
        {
            Calls.Add(content.ItemId);
            if (Frames == 0)
            {
                return Task.FromResult(FrameSample.Failed(new ExtractionWarning(FrameSamplingWarnings.FrameSamplingFfmpegUnavailable)));
            }

            var frames = Enumerable.Range(0, Frames).Select(index => new SampledFrame
            {
                Index = index,
                Timestamp = TimeSpan.FromSeconds(index),
                PerceptualHash = (ulong)index,
                Width = 4,
                Height = 2,
                Pixels = Enumerable.Repeat((byte)(index * 40), 4 * 2 * 4).ToArray(),
            }).ToList();
            return Task.FromResult(new FrameSample { Frames = frames, FramesRead = Frames });
        }
    }

    private sealed class UsageStore : IModelStore
    {
        public event EventHandler? ModelsChanged
        {
            add { }
            remove { }
        }

        public string RootDirectory => "models";

        public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => Task.FromResult<InstalledModel?>(null);

        public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InstalledModel>>([]);

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
