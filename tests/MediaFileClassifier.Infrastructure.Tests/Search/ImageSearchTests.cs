using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using static MediaFileClassifier.Infrastructure.Tests.Media.FormatTestSupport;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for the image-to-image search of <see cref="SearchMediaQueryHandler"/> (E09-S04-T02) against a real library
/// and search index: a fake image embedder whose query vector the tests place items around, and the real SigLIP 2
/// model when it's installed.
/// </summary>
public sealed class ImageSearchTests : IAsyncLifetime
{
    private const int Dimension = 8;
    private const string ReferenceKey = "reference picture";

    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await SearchTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Search_ReferenceImage_RanksBySimilarity_IgnoresTheText_AndShowsEveryNeighbour()
    {
        var provider = ImageProvider();
        var model = await RegisterAsync(provider);
        var close = await AddAsync(model, 1, 0.95f, title: "unrelated words");
        var middle = await AddAsync(model, 2, 0.6f);
        var far = await AddAsync(model, 3, 0.05f);
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { Text = "unrelated words", ReferenceImage = Reference() });

        Assert.Equal([close.Id, middle.Id, far.Id], results.Hits.Select(hit => hit.ItemId));
        Assert.Equal(SearchMode.Image, results.Mode);
        Assert.Equal(SemanticSearchStatus.Used, results.Semantic);
        Assert.Equal(3, results.TotalCount);
        Assert.Empty(results.Terms);
        Assert.Equal(0, results.LessRelevantCount);
        Assert.All(results.Hits, hit => Assert.False(hit.IsLessRelevant));
        Assert.Equal(0.95f, results.Hits[0].Similarity!.Value, 3);
    }

    [Fact]
    public async Task Search_ReferenceImage_AppliesTheFilters()
    {
        var provider = ImageProvider();
        var model = await RegisterAsync(provider);
        await AddAsync(model, 1, 0.95f);
        var favorite = await AddAsync(model, 2, 0.5f, item => item.SetFavorite(true, bed.Library.Clock.Tick()));
        using var handler = Create(provider);

        var results = await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = Reference(), Filter = new SearchFilter { IsFavorite = true } });

        Assert.Equal([favorite.Id], results.Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Search_ReferenceImage_IsEncodedOnceForAllPages_AndNotAddedToTheLibrary()
    {
        var provider = ImageProvider();
        var model = await RegisterAsync(provider);
        for (var i = 1; i <= 5; i++)
        {
            await AddAsync(model, i, 0.9f - (i * 0.1f));
        }

        var before = (await bed.Library.Library.Items.GetIdentitiesAsync(Token)).Count;
        using var handler = Create(provider);
        var reference = Reference();

        var first = await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = reference, Limit = 2 });
        var second = await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = reference, Offset = 2, Limit = 2 });

        Assert.Equal(1, provider.Calls);
        Assert.Equal(5, first.TotalCount);
        Assert.Empty(first.Hits.Select(hit => hit.ItemId).Intersect(second.Hits.Select(hit => hit.ItemId)));
        Assert.Equal(before, (await bed.Library.Library.Items.GetIdentitiesAsync(Token)).Count);

        // Another picture, even with the same bytes, is encoded again.
        await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = Reference() });
        Assert.Equal(2, provider.Calls);
    }

    [Theory]
    [InlineData(AiFailureKind.ModelMissing, SemanticSearchStatus.ModelMissing)]
    [InlineData(AiFailureKind.InvalidInput, SemanticSearchStatus.Failed)]
    [InlineData(AiFailureKind.ProviderError, SemanticSearchStatus.Failed)]
    public async Task Search_ReferenceImageCannotBeEncoded_ReturnsNothing_WithTheReason(AiFailureKind failure, SemanticSearchStatus expected)
    {
        var model = await RegisterAsync(ImageProvider());
        await AddAsync(model, 1, 0.9f);
        using var handler = Create(new FakeEmbeddingProvider(new FakeProviderBehavior { FailureRate = 1, FailureKind = failure }, Dimension, imageKey: _ => ReferenceKey));

        var results = await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = Reference() });

        Assert.Empty(results.Hits);
        Assert.Equal(0, results.TotalCount);
        Assert.Equal(SearchMode.Image, results.Mode);
        Assert.Equal(expected, results.Semantic);
    }

    [Fact]
    public async Task Search_ReferenceImageWithoutAProvider_IsUnavailable()
    {
        await bed.AddItemAsync(1);
        using var handler = Create(null);

        var results = await handler.SearchAsync(new SearchMediaQuery { ReferenceImage = Reference() });

        Assert.Empty(results.Hits);
        Assert.Equal(SemanticSearchStatus.Unavailable, results.Semantic);
    }

    [Fact]
    public async Task Search_ScreenshotOfALibraryImage_RealModel_FindsTheOriginalFirst()
    {
        var directory = Siglip2TestModel.RequireDirectory();
        using var provider = new Siglip2EmbeddingProvider(new Siglip2StubStore(Siglip2TestModel.CreateInstalled(directory)), new OnnxSessionFactory(), new Siglip2Options());
        var registry = new AiProviderRegistry([provider], bed.Library.Clock);
        var analyzer = new ItemEmbeddingAnalyzer(registry, new StillsOnlySampler(), new Siglip2StubStore(null), bed.Library.Clock);
        var files = Siglip2TestModel.Reference.Images.Select(image => Path.Combine(SampleMedia.Directory, image.File)).ToList();
        var items = new List<MediaItem>();
        for (var i = 0; i < files.Count; i++)
        {
            var item = await bed.AddItemAsync(i + 1);
            items.Add(item);
            await analyzer.EmbedStillAsync(bed.Library.Library, item.Id, new AiImage(await File.ReadAllBytesAsync(files[i], Token), "image/*", files[i]), Token);
        }

        using var encoder = new SemanticQueryEncoder(registry, bed.Session, bed.Library.Clock);
        var handler = new SearchMediaQueryHandler(bed.Index, encoder, registry, NullLogger<SearchMediaQueryHandler>.Instance);
        var load = new LoadReferenceImageQueryHandler(CreateRegistry());
        for (var i = 0; i < files.Count; i++)
        {
            // A screenshot: smaller, inside a window frame, saved as PNG under another name.
            var screenshot = await load.Handle(new LoadReferenceImageQuery(Screenshot(files[i]), "Screenshot 2026-11-11.png"), Token);
            Assert.NotNull(screenshot.Image);

            var results = await handler.Handle(new SearchMediaQuery { ReferenceImage = screenshot.Image }, Token);

            Assert.Equal(SemanticSearchStatus.Used, results.Semantic);
            Assert.True(items[i].Id == results.Hits[0].ItemId, $"{files[i]}: the original is at {results.Hits.Select(hit => hit.ItemId).ToList().IndexOf(items[i].Id)}, similarity {results.Hits[0].Similarity}");
        }
    }

    private static ReferenceImage Reference() => new(new AiImage(new byte[] { 1, 2, 3 }, "image/png", "reference.png"), "reference.png");

    private static FakeEmbeddingProvider ImageProvider() => new(dimension: Dimension, imageKey: _ => ReferenceKey);

    // Scales the picture to 80%, puts it in a light window frame with a dark title bar (chrome in proportion to the
    // picture, as when a displayed image is captured), and encodes it as PNG.
    private static byte[] Screenshot(string file)
    {
        using var original = SKBitmap.Decode(file);
        var width = (int)(original.Width * 0.8);
        var height = (int)(original.Height * 0.8);
        var margin = Math.Max(4, width / 32);
        var titleBar = Math.Max(8, height / 20);
        using var surface = SKSurface.Create(new SKImageInfo(width + (2 * margin), height + titleBar + (2 * margin)));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xF3, 0xF3, 0xF3));
        using (var bar = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20) })
        {
            canvas.DrawRect(0, 0, width + (2 * margin), titleBar, bar);
        }

        using var image = SKImage.FromBitmap(original);
        canvas.DrawImage(image, SKRect.Create(margin, titleBar + margin, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private async Task<ModelRegistration> RegisterAsync(IAiProvider provider) =>
        await bed.Library.Library.Models.GetOrAddAsync(provider.Model, bed.Library.Clock.GetUtcNow(), Token);

    private async Task<MediaItem> AddAsync(ModelRegistration model, int number, float similarity, Action<MediaItem>? configure = null, string? title = null)
    {
        var item = await bed.AddItemAsync(number, title, configure: configure);
        await bed.Library.Library.Embeddings.ReplaceAsync(
            item.Id,
            model.Id,
            [Embedding.Create(item.Id, model, EmbeddingKind.Image, SearchMediaQueryHandlerTests.Around(ReferenceKey, similarity), bed.Library.Clock.Tick())],
            Token);
        return item;
    }

    private TestHandler Create(FakeEmbeddingProvider? provider)
    {
        var registry = new AiProviderRegistry(provider is null ? [] : [provider], bed.Library.Clock);
        var encoder = new SemanticQueryEncoder(registry, bed.Session, bed.Library.Clock);
        return new TestHandler(encoder, new SearchMediaQueryHandler(bed.Index, encoder, registry, NullLogger<SearchMediaQueryHandler>.Instance));
    }

    private sealed class TestHandler(SemanticQueryEncoder encoder, SearchMediaQueryHandler handler) : IDisposable
    {
        public Task<MediaSearchResults> SearchAsync(SearchMediaQuery query) => handler.Handle(query, Token);

        public void Dispose() => encoder.Dispose();
    }

    private sealed class StillsOnlySampler : IFrameSampler
    {
        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException("These tests embed stills only.");
    }
}
