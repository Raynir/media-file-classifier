using System.Diagnostics;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.Embeddings;

/// <summary>
/// Tests for <see cref="SemanticQueryEncoder"/> (E06-S03-T02) against a real library, with a fake embedding model.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class SemanticQueryEncoderTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task EncodeText_SameQueryInAnyCase_ComesFromTheCache()
    {
        var provider = new PreloadableEmbedder();
        using var encoder = Create(provider, new TestSession(bed.Library));

        var first = await encoder.EncodeTextAsync("A Red Car", Token);
        var again = await encoder.EncodeTextAsync("  a red car", Token);

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal(1, provider.Inner.Calls);
        Assert.Equal(1, encoder.CachedCount);
        Assert.Equal(1.0, Norm(first.Vector.Span), 3);
        Assert.Single(await bed.Library.Models.GetAllAsync(Token), model => provider.Model.Matches(model));
    }

    [Fact]
    public async Task EncodeText_MoreQueriesThanTheCapacity_DropsTheLeastRecentlyUsed()
    {
        var provider = new PreloadableEmbedder();
        using var encoder = Create(provider, new TestSession(bed.Library));
        await encoder.EncodeTextAsync("first", Token);
        for (var i = 0; i < SemanticQueryEncoder.CacheCapacity - 1; i++)
        {
            await encoder.EncodeTextAsync($"query {i}", Token);
        }

        await encoder.EncodeTextAsync("first", Token); // Now the most recent.
        await encoder.EncodeTextAsync("one more", Token); // Pushes out "query 0".
        var calls = provider.Inner.Calls;
        await encoder.EncodeTextAsync("first", Token);
        await encoder.EncodeTextAsync("query 0", Token);

        Assert.Equal(SemanticQueryEncoder.CacheCapacity, encoder.CachedCount);
        Assert.Equal(calls + 1, provider.Inner.Calls);
    }

    [Fact]
    public async Task EncodeText_NoLibraryOrNoProvider_ReturnsNull()
    {
        using var noLibrary = Create(new PreloadableEmbedder(), new TestSession(null));
        using var noProvider = new SemanticQueryEncoder(new AiProviderRegistry([], bed.Clock), new TestSession(bed.Library), bed.Clock);

        Assert.Null(await noLibrary.EncodeTextAsync("cat", Token));
        Assert.Null(await noProvider.EncodeTextAsync("cat", Token));
    }

    [Fact]
    public async Task LibraryOpenedAndClosed_PreloadsThenUnloadsTheModelAndClearsTheCache()
    {
        var provider = new PreloadableEmbedder();
        var session = new TestSession(null);
        using var encoder = Create(provider, session);
        Assert.Equal(0, provider.Preloads);

        await session.SetCurrentAsync(bed.Library);
        await encoder.WaitForPreloadAsync();
        await encoder.EncodeTextAsync("cat", Token);

        Assert.Equal(1, provider.Preloads);
        Assert.Equal(AiCapability.TextEmbedding, provider.PreloadedCapability);
        Assert.Equal(1, encoder.CachedCount);

        await session.SetCurrentAsync(null);

        Assert.Equal(1, provider.Unloads);
        Assert.Equal(0, encoder.CachedCount);
    }

    [Fact]
    public async Task LibraryOpened_ModelMissing_DoesNotThrow()
    {
        var provider = new PreloadableEmbedder { PreloadError = new AiProviderException(AiFailureKind.ModelMissing, "fake", "not installed") };
        var session = new TestSession(null);
        using var encoder = Create(provider, session);

        await session.SetCurrentAsync(bed.Library);
        await encoder.WaitForPreloadAsync();

        Assert.Equal(1, provider.Preloads);
    }

    [Fact]
    public async Task EncodeText_RealModel_MedianUnderFiftyMilliseconds()
    {
        var directory = Siglip2TestModel.RequireDirectory();
        var store = new Siglip2StubStore(Siglip2TestModel.CreateInstalled(directory));
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(), new Siglip2Options());
        var session = new TestSession(bed.Library);
        using var encoder = new SemanticQueryEncoder(new AiProviderRegistry([provider], bed.Clock), session, bed.Clock);
        await encoder.WaitForPreloadAsync();
        await encoder.EncodeTextAsync("warm up", Token);

        // The class runs alone in this assembly (timing collection) and the best of three rounds absorbs noise. Other test
        // projects and a loaded LM Studio model (GPU memory) can still slow it: unload models before a full run.
        var medians = new List<double>();
        for (var round = 0; round < 3 && (medians.Count == 0 || medians.Min() >= 50); round++)
        {
            var times = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var started = Stopwatch.GetTimestamp();
                var vector = await encoder.EncodeTextAsync($"a photo of a dog number {round}-{i}", Token);
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                Assert.Equal(Siglip2Model.Dimension, vector!.Vector.Length);
            }

            times.Sort();
            medians.Add(times[10]);
        }

        TestContext.Current.SendDiagnosticMessage($"text query: median {medians.Min():F1} ms");
        if (TestPerformance.Enabled)
        {
            Assert.True(medians.Min() < 50, $"median {medians.Min():F1} ms");
        }
    }

    [Fact]
    public async Task EncodeImage_ExternalFile_DoesNotTouchTheLibrary()
    {
        var provider = new PreloadableEmbedder();
        using var encoder = Create(provider, new TestSession(bed.Library));
        var before = (await bed.Library.Items.GetIdentitiesAsync(Token)).Count;

        var vector = await encoder.EncodeImageFileAsync(SampleMedia.PngWithAlpha, Token);

        Assert.NotNull(vector);
        Assert.Equal(1.0, Norm(vector.Vector.Span), 3);
        Assert.Null(vector.ItemId);
        Assert.Equal(before, (await bed.Library.Items.GetIdentitiesAsync(Token)).Count);
        Assert.Equal(0, encoder.CachedCount);
    }

    [Fact]
    public async Task EncodeImage_RealModel_FindsTheSameAndSimilarPicturesFirst()
    {
        var directory = Siglip2TestModel.RequireDirectory();
        var store = new Siglip2StubStore(Siglip2TestModel.CreateInstalled(directory));
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(), new Siglip2Options());
        await using var search = await SearchTestBed.CreateAsync();
        var registry = new AiProviderRegistry([provider], search.Library.Clock);
        var analyzer = new ItemEmbeddingAnalyzer(registry, new StillsOnlySampler(), new Siglip2StubStore(null), search.Library.Clock);
        using var encoder = new SemanticQueryEncoder(registry, search.Session, search.Library.Clock);
        var files = Siglip2TestModel.Reference.Images.Select(image => Path.Combine(SampleMedia.Directory, image.File)).ToList();
        var items = new List<MediaItem>();
        for (var i = 0; i < files.Count; i++)
        {
            var item = await search.AddItemAsync(i + 1);
            items.Add(item);
            await analyzer.EmbedStillAsync(search.Library.Library, item.Id, new AiImage(await File.ReadAllBytesAsync(files[i], Token), "image/*", files[i]), Token);
        }

        for (var i = 0; i < files.Count; i++)
        {
            var query = await encoder.EncodeImageFileAsync(files[i], Token);
            var results = await search.SearchAsync(new SearchQuery { Vector = query });

            Assert.Equal(items[i].Id, results.Hits[0].ItemId);
            Assert.True(results.Hits[0].Similarity > 0.999f, $"{files[i]}: similarity {results.Hits[0].Similarity}");
        }
    }

    private static double Norm(ReadOnlySpan<float> vector)
    {
        var sum = 0.0;
        foreach (var value in vector)
        {
            sum += (double)value * value;
        }

        return Math.Sqrt(sum);
    }

    private SemanticQueryEncoder Create(PreloadableEmbedder provider, TestSession session) =>
        new(new AiProviderRegistry([provider], bed.Clock), session, bed.Clock);

    private sealed class StillsOnlySampler : IFrameSampler
    {
        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests embed stills only.");
    }

    /// <summary>
    /// A fake embedder that also counts preloads and unloads.
    /// </summary>
    private sealed class PreloadableEmbedder : ITextEmbeddingProvider, IImageEmbeddingProvider, IPreloadableAiProvider
    {
        public FakeEmbeddingProvider Inner { get; } = new(dimension: 8);

        public Exception? PreloadError { get; init; }

        public int Preloads { get; private set; }

        public int Unloads { get; private set; }

        public AiCapability? PreloadedCapability { get; private set; }

        public string Id => Inner.Id;

        public string DisplayName => Inner.DisplayName;

        public AiProviderKind Kind => Inner.Kind;

        public ModelIdentity Model => Inner.Model;

        public bool IsSupportedOnCurrentPlatform => true;

        public int Dimension => Inner.Dimension;

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Inner.CheckHealthAsync(cancellationToken);

        public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Inner.EmbedTextsAsync(texts, cancellationToken);

        public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken) =>
            Inner.EmbedImagesAsync(images, cancellationToken);

        public Task PreloadAsync(AiCapability? capability, CancellationToken cancellationToken)
        {
            Preloads++;
            PreloadedCapability = capability;
            return PreloadError is null ? Task.CompletedTask : Task.FromException(PreloadError);
        }

        public void Unload() => Unloads++;
    }
}
