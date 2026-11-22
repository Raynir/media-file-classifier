using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Formats;
using MediaFileClassifier.Infrastructure.Abstractions.Frames;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Metadata;
using MediaFileClassifier.Infrastructure.Media.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.UseCases.Analysis.Keyframes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// Tests for <see cref="ItemKeyframeSampler"/>, the one place that samples an item's keyframes and stores their rows
/// and pictures (MFC-1008).
/// </summary>
public sealed class ItemKeyframeSamplerTests : IAsyncLifetime
{
    private readonly StubSampler sampler = new();
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task SampleAndStore_SamplesOnce_AndStoresTheRowsAndPicturesOfTheSameFrames()
    {
        var item = await AddItemAsync();

        var frames = await Create().SampleAndStoreAsync(bed.Library, Content(item), Format, Metadata(item), Token);

        Assert.Equal(1, sampler.Calls);
        Assert.Equal(3, frames.Frames.Count);
        Assert.Equal(
            frames.Frames.Select(frame => frame.Keyframe.Id),
            (await bed.Library.Keyframes.GetForItemAsync(item.Id, Token)).Select(keyframe => keyframe.Id));
        Assert.All(frames.Frames, frame => Assert.Equal(4 * 2 * 4, frame.Image.Data.Length));
        Assert.Equal(3, Pictures(item).Length);
    }

    [Fact]
    public async Task SampleAndStore_WithoutFrames_ReturnsTheWarnings_AndKeepsTheEarlierKeyframes()
    {
        var item = await AddItemAsync();
        var keyframeSampler = Create();
        var earlier = await keyframeSampler.SampleAndStoreAsync(bed.Library, Content(item), Format, Metadata(item), Token);
        sampler.Fail = true;

        var frames = await keyframeSampler.SampleAndStoreAsync(bed.Library, Content(item), Format, Metadata(item), Token);

        Assert.Empty(frames.Frames);
        Assert.Equal(FrameSamplingWarnings.FrameSamplingFailed, Assert.Single(frames.Warnings).Code);
        Assert.Equal(
            earlier.Frames.Select(frame => frame.Keyframe.Id),
            (await bed.Library.Keyframes.GetForItemAsync(item.Id, Token)).Select(keyframe => keyframe.Id));
        Assert.Equal(3, Pictures(item).Length);
    }

    [Fact]
    public async Task SampleAndStore_WithoutAPictureStore_StoresOnlyTheRows()
    {
        var item = await AddItemAsync();

        var frames = await new ItemKeyframeSampler(sampler).SampleAndStoreAsync(bed.Library, Content(item), Format, Metadata(item), Token);

        Assert.Equal(3, (await bed.Library.Keyframes.GetForItemAsync(item.Id, Token)).Count);
        Assert.Equal(3, frames.Frames.Count);
        Assert.Empty(Pictures(item));
    }

    private static MediaFormatDetection Format => new() { MediaType = MediaType.Video };

    private static MediaContent Content(MediaItem item) =>
        new(item.Id, DateTimeOffset.UnixEpoch, _ => throw new InvalidOperationException("The stub sampler doesn't read the file."));

    private static TechnicalMetadata Metadata(MediaItem item) => new() { ItemId = item.Id, ExtractedAt = DateTimeOffset.UnixEpoch };

    private ItemKeyframeSampler Create() => new(sampler, new KeyframeThumbnails(NullLogger<KeyframeThumbnails>.Instance));

    private string[] Pictures(MediaItem item)
    {
        var folder = Path.GetDirectoryName(bed.Library.Layout.GetKeyframeThumbnailPath(item.Id, 0))!;
        return Directory.Exists(folder) ? Directory.GetFiles(folder, LibraryLayout.KeyframeThumbnailSearchPattern(item.Id)) : [];
    }

    private async Task<MediaItem> AddItemAsync()
    {
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private sealed class StubSampler : IFrameSampler
    {
        public int Calls { get; private set; }

        public bool Fail { get; set; }

        public Task<FrameSample> SampleAsync(MediaContent content, MediaFormatDetection format, TechnicalMetadata metadata, FrameSamplingOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                return Task.FromResult(FrameSample.Failed(new ExtractionWarning(FrameSamplingWarnings.FrameSamplingFailed)));
            }

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
