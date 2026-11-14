using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Infrastructure.Tests.Media;
using MediaFileClassifier.Testing.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The core filters (E10-S03) on the sample media corpus, imported by the real pipeline, so the values they filter on
/// come from the real classification and metadata extraction.
/// </summary>
public sealed class CoreFilterCorpusTests(CoreFilterCorpusTests.Corpus corpus) : IClassFixture<CoreFilterCorpusTests.Corpus>
{
    private static readonly string[] Videos =
        ["video-action.mp4", "video-av1.mp4", "video-black-intro.mp4", "video-hevc.mp4", "video-portrait.mp4", "video-short.mp4", "video-static-camera.mp4"];

    private static readonly string[] AnimatedImages =
        ["gif-animated.gif", "gif-blank-first-frame.gif", "gif-three-frames.gif", "gif-variable-delays.gif", "webp-animated.webp"];

    private static readonly string[] StillImages =
        ["corrupt.jpg", "gif-static.gif", "jpeg-exif.jpg", "jpeg-icc-offset.jpg", "ocr-text.png", "png-alpha.png", "webp.webp", "фото с пробелом.jpg"];

    private static MediaTypeFilter Types { get; } = new();

    private static FileFormatFilter Formats { get; } = new(FormatTestSupport.CreateRegistry());

    public static TheoryData<string[], string[]> MediaTypeCases() => new()
    {
        { ["Image"], StillImages },
        { ["AnimatedImage"], AnimatedImages },
        { ["Video"], Videos },
        { ["Image", "AnimatedImage"], [.. StillImages, .. AnimatedImages] },
        { ["Image", "AnimatedImage", "Video"], [.. StillImages, .. AnimatedImages, .. Videos] },
    };

    public static TheoryData<string[], string[]> FileFormatCases() => new()
    {
        { ["jpeg"], ["corrupt.jpg", "jpeg-exif.jpg", "jpeg-icc-offset.jpg", "фото с пробелом.jpg"] },
        { ["png"], ["ocr-text.png", "png-alpha.png"] },
        { ["webp"], ["webp.webp", "webp-animated.webp"] },
        { ["gif"], ["gif-animated.gif", "gif-blank-first-frame.gif", "gif-static.gif", "gif-three-frames.gif", "gif-variable-delays.gif"] },
        { ["mp4"], Videos },
        { ["gif", "webp"], ["gif-animated.gif", "gif-blank-first-frame.gif", "gif-static.gif", "gif-three-frames.gif", "gif-variable-delays.gif", "webp.webp", "webp-animated.webp"] },
    };

    [Fact]
    public void Corpus_IsImportedAsExpected()
    {
        // Guards the cases below: every media file is an item, and the files that aren't media are not.
        Assert.Equal(StillImages.Concat(AnimatedImages).Concat(Videos).Order(StringComparer.Ordinal), corpus.Names.Values.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MediaTypeCases))]
    public async Task MediaTypeFilter_KeepsTheItemsOfTheTypes(string[] values, string[] expected)
    {
        var names = await corpus.SearchAsync(new FilterCondition(Types, new MultiSelectValue(values)));

        Assert.Equal(expected.Order(StringComparer.Ordinal), names, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FileFormatCases))]
    public async Task FileFormatFilter_KeepsTheItemsOfTheFormats(string[] values, string[] expected)
    {
        var names = await corpus.SearchAsync(new FilterCondition(Formats, new MultiSelectValue(values)));

        Assert.Equal(expected.Order(StringComparer.Ordinal), names, StringComparer.Ordinal);
    }

    [Fact]
    public async Task MediaTypeAndFileFormat_Combine()
    {
        var names = await corpus.SearchAsync(
            new FilterCondition(Types, new MultiSelectValue(["Image"])),
            new FilterCondition(Formats, new MultiSelectValue(["gif", "webp"])));

        Assert.Equal(["gif-static.gif", "webp.webp"], names, StringComparer.Ordinal);
    }

    /// <summary>
    /// The top level of the sample corpus (the robustness set adds nothing these filters need), imported once.
    /// </summary>
    public sealed class Corpus : IAsyncLifetime
    {
        private ImportTestBed bed = null!;
        private SearchIndex index = null!;

        /// <summary>
        /// Gets the file name of every item.
        /// </summary>
        public Dictionary<Id<MediaItem>, string> Names { get; } = [];

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        /// Lists the items that pass every condition.
        /// </summary>
        /// <param name="conditions">The conditions.</param>
        /// <returns>Their file names, sorted.</returns>
        public async Task<IReadOnlyList<string>> SearchAsync(params FilterCondition[] conditions)
        {
            var results = await index.SearchAsync(new SearchQuery { Filter = new SearchFilter { Conditions = conditions }, Limit = 1000 }, Token);
            Assert.Equal(results.Hits.Count, results.TotalCount);
            return [.. results.Hits.Select(hit => Names[hit.ItemId]).Order(StringComparer.Ordinal)];
        }

        public async ValueTask InitializeAsync()
        {
            bed = await ImportTestBed.CreateAsync();
            foreach (var file in Directory.EnumerateFiles(SampleMedia.Directory))
            {
                File.Copy(file, Path.Combine(bed.Root, Path.GetFileName(file)));
            }

            await bed.ImportAsync();
            await bed.WaitUntilIdleAsync();
            foreach (var item in await bed.ItemsAsync())
            {
                Names[item.Id] = item.Path.Value;
            }

            index = new SearchIndex(new TestSession(bed.Library), NullLogger<SearchIndex>.Instance);
            await index.Ready;
        }

        public async ValueTask DisposeAsync()
        {
            index?.Dispose();
            await bed.DisposeAsync();
        }
    }
}
