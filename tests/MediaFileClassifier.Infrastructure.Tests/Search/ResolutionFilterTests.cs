using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The resolution filter (E10-S03-T03): presets on the short and long side, and custom minimums on the displayed width
/// and height, so rotated portrait photos are filtered as they're shown.
/// </summary>
public sealed class ResolutionFilterTests(ResolutionFilterTests.Library library) : IClassFixture<ResolutionFilterTests.Library>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string?, int?, int?, string[]> Cases() => new()
    {
        { ResolutionFilter.Small, null, null, ["small"] },
        { ResolutionFilter.Hd, null, null, ["almost-4k", "landscape-hd", "portrait-rotated", "portrait-stored", "transverse", "uhd"] },
        { ResolutionFilter.FullHd, null, null, ["almost-4k", "portrait-rotated", "portrait-stored", "transverse", "uhd"] },
        { ResolutionFilter.UltraHd, null, null, ["portrait-rotated", "uhd"] },

        // An unknown preset, e.g. from a newer version.
        { "8k", null, null, [] },

        // Displayed width: the rotated portrait is 3024 wide, the transverse one 4100.
        { null, 3000, null, ["almost-4k", "portrait-rotated", "transverse", "uhd"] },

        // Displayed height: only the rotated portrait is 4032 high; stored, it is 3024.
        { null, null, 4000, ["portrait-rotated"] },
        { null, 1000, 1500, ["almost-4k", "portrait-rotated", "portrait-stored", "transverse", "uhd"] },
        { null, 800, 600, ["almost-4k", "edge-small", "landscape-hd", "portrait-rotated", "portrait-stored", "transverse", "uhd"] },

        // A preset wins over minimums.
        { ResolutionFilter.Small, 3000, null, ["small"] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Filter_KeepsTheItemsOfTheDisplayedResolution(string? preset, int? minWidth, int? minHeight, string[] expected)
    {
        var names = await library.SearchAsync(new ResolutionValue(preset, minWidth, minHeight));

        Assert.Equal(expected, names);
    }

    [Fact]
    public void Presets_ListThePresets()
    {
        var options = new ResolutionFilter().Presets;

        Assert.Equal([ResolutionFilter.Small, ResolutionFilter.Hd, ResolutionFilter.FullHd, ResolutionFilter.UltraHd], options.Select(option => option.Id));
        Assert.All(options, option => Assert.StartsWith("Filter_Resolution_", option.DisplayNameKey, StringComparison.Ordinal));
    }

    [Fact]
    public void ToSql_PassesEveryValueAsAParameter()
    {
        var sql = new Infrastructure.DataAccess.Search.SearchSql();

        var condition = new ResolutionFilter().ToSql(new ResolutionValue(null, 1234, 5678), sql);

        Assert.NotNull(condition);
        Assert.DoesNotContain("1234", condition, StringComparison.Ordinal);
        Assert.Equal([1234, 5678], sql.Parameters.Select(parameter => parameter.Value));
        Assert.True(sql.NeedsTechnicalMetadata);
    }

    /// <summary>
    /// Items of several sizes and orientations; their file names are the names the cases use.
    /// </summary>
    public sealed class Library : IAsyncLifetime
    {
        private readonly Dictionary<Id<MediaItem>, string> names = [];

        private SearchTestBed bed = null!;

        public async Task<string[]> SearchAsync(ResolutionValue value)
        {
            var filter = new SearchFilter { Conditions = [new FilterCondition(new ResolutionFilter(), value)] };
            var results = await bed.SearchAsync(new SearchQuery { Filter = filter });
            return [.. results.Hits.Select(hit => names[hit.ItemId]).Order(StringComparer.Ordinal)];
        }

        public async ValueTask InitializeAsync()
        {
            bed = await SearchTestBed.CreateAsync();
            await AddAsync("landscape-hd", 1280, 720);

            // A phone photo stored landscape, shown portrait (3024 × 4032) by its EXIF orientation.
            await AddAsync("portrait-rotated", 4032, 3024, ImageOrientation.Rotate90);
            await AddAsync("portrait-stored", 1080, 1920);
            await AddAsync("small", 640, 480);
            await AddAsync("edge-small", 800, 600);
            await AddAsync("uhd", 3840, 2160);
            await AddAsync("almost-4k", 3840, 2159);

            // Transverse swaps the axes too: shown 4100 × 2000.
            await AddAsync("transverse", 2000, 4100, ImageOrientation.Transverse);
            await AddAsync("no-metadata", null, null);
        }

        public ValueTask DisposeAsync() => bed.DisposeAsync();

        private async Task AddAsync(string name, int? width, int? height, ImageOrientation orientation = ImageOrientation.Normal)
        {
            var item = MediaItem.Discover(
                bed.Library.Folder.Id,
                RelativePath.From(name + ".jpg"),
                MediaType.Image,
                1000,
                LibraryTestBed.Start,
                LibraryTestBed.Start,
                (UInt128)names.Count + 1,
                LibraryTestBed.Start);
            await bed.Library.Library.Items.AddAsync(item, Token);
            if (width is not null)
            {
                await bed.Library.Library.Items.UpdateAsync(
                    item,
                    new TechnicalMetadata { ItemId = item.Id, Width = width, Height = height, Orientation = orientation, ExtractedAt = LibraryTestBed.Start },
                    Token);
            }

            names[item.Id] = name;
        }
    }
}
