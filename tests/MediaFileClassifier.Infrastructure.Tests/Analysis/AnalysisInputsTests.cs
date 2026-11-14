using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.AI.Imaging;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Analysis.Pipeline;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for <see cref="AnalysisInputs"/> and <see cref="SkiaAnalysisImageDecoder"/>: one decode or one sampling per
/// item feeds every analyzer (E06-S01-T02).
/// </summary>
public sealed class AnalysisInputsTests : IAsyncLifetime
{
    private readonly CountingDecoder decoder = new();
    private readonly CountingSampler sampler = new();
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Still_OneDecodeFeedsEveryAnalyzerSize()
    {
        var item = await AddItemAsync();
        var inputs = Inputs(item, SampleMedia.JpegWithExif, MediaType.Image, new InputSize(512, 2048));

        var shared = await inputs.GetStillAsync(null, Token);
        var describer = await inputs.GetStillAsync(64, Token);
        var again = await inputs.GetStillAsync(64, Token);
        var ocr = await inputs.GetStillAsync(100_000, Token);

        Assert.Equal(1, decoder.Decodes);
        Assert.Equal(64, describer.LongestEdge);
        Assert.Same(describer, again);
        Assert.Same(shared, ocr);
        Assert.Equal(describer.Width * describer.Height * 4, describer.Pixels.Length);
        Assert.Empty(sampler.Calls);
    }

    [Fact]
    public async Task Video_SampledAndStoredOnce()
    {
        var item = await AddItemAsync();
        var inputs = Inputs(item, SampleMedia.ActionVideo, MediaType.Video, new InputSize(512, 0));

        var first = await inputs.GetFramesAsync(Token);
        var second = await inputs.GetFramesAsync(Token);

        Assert.Same(first, second);
        Assert.Single(sampler.Calls);
        Assert.Equal(0, decoder.Decodes);
        Assert.Equal(first.Frames.Select(frame => frame.Keyframe.Id), (await bed.Library.Keyframes.GetForItemAsync(item.Id, Token)).Select(keyframe => keyframe.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inputs.GetStillAsync(null, Token));
    }

    [Theory]
    [InlineData(0, 0, 240)]
    [InlineData(512, 0, 960)]
    [InlineData(0, 2048, 1920)]
    public void Decoder_DecodesAtTheSmallestSizeThatMeetsTheLimits(int minShortSide, int minLongestEdge, int expectedShortSide)
    {
        // A 2560 x 1920 JPEG: DCT scaling gives 320 x 240, 640 x 480 or 1280 x 960, or the full size.
        var jpeg = Jpeg(2560, 1920);

        var image = new SkiaAnalysisImageDecoder().Decode(jpeg, minShortSide, minLongestEdge);

        Assert.Equal(expectedShortSide, Math.Min(image.Width, image.Height));
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(SkiaSharp.SKColors.CadetBlue);
        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    private AnalysisInputs Inputs(MediaItem item, string path, MediaType type, InputSize size) =>
        new(
            decoder,
            sampler,
            bed.Library,
            new MediaContent(item.Id, DateTimeOffset.UnixEpoch, _ => Task.FromResult<Stream>(File.OpenRead(path))) { LocalPath = path },
            new MediaFormatDetection { MediaType = type },
            new TechnicalMetadata { ItemId = item.Id, ExtractedAt = DateTimeOffset.UnixEpoch },
            size);

    private async Task<MediaItem> AddItemAsync()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private sealed class CountingDecoder : IAnalysisImageDecoder
    {
        private readonly SkiaAnalysisImageDecoder inner = new();

        public int Decodes { get; private set; }

        public DecodedImage Decode(ReadOnlyMemory<byte> encoded, int minShortSide, int minLongestEdge, string? name = null)
        {
            Decodes++;
            return inner.Decode(encoded, minShortSide, minLongestEdge, name);
        }

        public DecodedImage Shrink(DecodedImage image, int maxLongestEdge) => inner.Shrink(image, maxLongestEdge);
    }

    private sealed class CountingSampler : IFrameSampler
    {
        public List<Id<MediaItem>> Calls { get; } = [];

        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken)
        {
            Calls.Add(content.ItemId);
            var frames = Enumerable.Range(0, 3).Select(index => new SampledFrame
            {
                Index = index,
                Timestamp = TimeSpan.FromSeconds(index),
                PerceptualHash = (ulong)index,
                Width = 4,
                Height = 2,
                Pixels = Enumerable.Repeat((byte)(index * 40), 4 * 2 * 4).ToArray(),
            }).ToList();
            return Task.FromResult(new FrameSample { Frames = frames, FramesRead = 3 });
        }
    }
}
