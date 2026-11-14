using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The processing status and folder filters (E10-S03-T04): each status, and folders and subfolders with and without
/// their subfolders, in an online and an Offline folder.
/// </summary>
public sealed class StatusAndFolderFilterTests(StatusAndFolderFilterTests.Library library) : IClassFixture<StatusAndFolderFilterTests.Library>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string[], string[]> StatusCases() => new()
    {
        { [StatusFilter.Pending], ["lookalike", "trip"] },
        { [StatusFilter.Indexed], ["offline-sub", "photo-lower", "photo-upper", "root", "year"] },
        { [StatusFilter.Error], ["percent"] },
        { [StatusFilter.Missing], ["offline-root"] },
        { [StatusFilter.Pending, StatusFilter.Error], ["lookalike", "percent", "trip"] },

        // Values ignore case; unknown ones (e.g. from a newer version) are ignored.
        { ["INDEXED", "excluded"], ["offline-sub", "photo-lower", "photo-upper", "root", "year"] },
        { ["excluded"], [] },
    };

    // "A" and "B" at the start stand for the online and the Offline folder's IDs.
    public static TheoryData<string[], bool, string[]> FolderCases() => new()
    {
        { ["A"], true, ["lookalike", "percent", "root", "trip", "year"] },
        { ["A"], false, ["root"] },
        { ["A/2024"], true, ["trip", "year"] },
        { ["A/2024"], false, ["year"] },
        { ["A/2024/Trips"], true, ["trip"] },

        // Backslashes and extra slashes are read as one separator; LIKE characters in names are plain text.
        { [@"A\2024\"], true, ["trip", "year"] },
        { ["A/100%"], true, ["percent"] },

        // Any of several folders, the Offline one included.
        { ["A/2024", "B"], true, ["offline-root", "offline-sub", "photo-lower", "photo-upper", "trip", "year"] },
        { ["B"], false, ["offline-root"] },

        // Folder names ignore case in every alphabet, as on Windows and in the folder tree.
        { ["B/ФОТО"], true, ["photo-lower", "photo-upper"] },
        { ["B/фото"], false, ["photo-lower", "photo-upper"] },

        // A folder ID that can't be read, or that no folder has, matches nothing.
        { ["not-a-folder"], true, [] },
        { ["00000000-0000-0000-0000-000000000001"], true, [] },
    };

    [Theory]
    [MemberData(nameof(StatusCases))]
    public async Task StatusFilter_KeepsTheItemsWithThoseStatuses(string[] values, string[] expected)
    {
        var names = await library.SearchAsync(new StatusFilter(), new MultiSelectValue(values));

        Assert.Equal(expected, names);
    }

    [Theory]
    [MemberData(nameof(FolderCases))]
    public async Task FolderFilter_KeepsTheItemsInThoseFolders(string[] folders, bool includeSubfolders, string[] expected)
    {
        var names = await library.SearchAsync(library.FolderFilter, new FolderValue([.. folders.Select(library.Resolve)], includeSubfolders));

        Assert.Equal(expected, names);
    }

    [Fact]
    public async Task StatusFilter_OffersFourStatuses()
    {
        var options = await new StatusFilter().GetOptionsAsync(null, Token);

        Assert.Equal([StatusFilter.Pending, StatusFilter.Indexed, StatusFilter.Error, StatusFilter.Missing], options.Select(option => option.Value));
        Assert.Equal(["Filter_Status_Pending", "Filter_Status_Indexed", "Filter_Status_Error", "Filter_Status_Missing"], options.Select(option => option.DisplayNameKey));
    }

    [Fact]
    public async Task FixedLists_IgnoreThePrefix_ThePanelNarrowsByTheShownText()
    {
        // e.g. the start of a translated label, which no value ID has.
        Assert.Equal(4, (await new StatusFilter().GetOptionsAsync("Ош", Token)).Count);
        Assert.Equal(Enum.GetNames<MediaType>().Length, (await new MediaTypeFilter().GetOptionsAsync("Ви", Token)).Count);
        Assert.Equal(4, (await new ResolutionFilter().GetOptionsAsync("x", Token)).Count);
    }

    [Fact]
    public async Task FolderOptions_ListTheIndexedFolders_MarkingOfflineOnes()
    {
        var options = await library.FolderFilter.GetOptionsAsync(null, Token);

        Assert.Equal(
            [new FilterOption(library.Resolve("A"), DisplayName: @"D:\Photos"), new FilterOption(library.Resolve("B"), DisplayName: @"E:\Phone", IsOffline: true)],
            options);
    }

    [Theory]
    [InlineData("A", new[] { "A/100%", "A/2024", "A/2024x" }, new[] { "100%", "2024", "2024x" })]
    [InlineData("A/2024", new[] { "A/2024/Trips" }, new[] { "Trips" })]
    [InlineData("A/2024/Trips", new string[0], new string[0])]
    [InlineData("B", new[] { "B/2024", "B/Фото" }, new[] { "2024", "Фото" })]
    [InlineData("not-a-folder", new string[0], new string[0])]
    public async Task FolderOptions_ListASubfoldersChildren(string prefix, string[] values, string[] names)
    {
        var options = await library.FolderFilter.GetOptionsAsync(library.Resolve(prefix), Token);

        Assert.Equal([.. values.Select(library.Resolve)], options.Select(option => option.Value));
        Assert.Equal(names, options.Select(option => option.DisplayName));
    }

    [Fact]
    public async Task FolderOptions_AreEmpty_WithoutALibrary()
    {
        var options = await new FolderFilter(new TestSession(null)).GetOptionsAsync(null, Token);

        Assert.Empty(options);
    }

    [Fact]
    public void FolderFilter_PassesTheFolderAndSubfolderAsParameters()
    {
        var sql = new Infrastructure.DataAccess.Search.SearchSql();

        var condition = library.FolderFilter.ToSql(new FolderValue([library.Resolve("A/Secret")], includeSubfolders: false), sql);

        Assert.NotNull(condition);
        Assert.DoesNotContain("Secret", condition, StringComparison.Ordinal);
        Assert.Equal([library.Resolve("A"), "SECRET/%", "SECRET/%/%"], sql.Parameters.Select(parameter => parameter.Value));
    }

    /// <summary>
    /// Items in an online folder (<c>D:\Photos</c>) and an Offline one (<c>E:\Phone</c>); the cases use their names.
    /// </summary>
    public sealed class Library : IAsyncLifetime
    {
        private readonly Dictionary<Id<MediaItem>, string> names = [];

        private SearchTestBed bed = null!;
        private IndexedFolder offline = null!;

        internal FolderFilter FolderFilter { get; private set; } = null!;

        /// <summary>
        /// Replaces a leading <c>A</c> or <c>B</c> with the online or the Offline folder's ID.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The value with the ID.</returns>
        public string Resolve(string value) => value switch
        {
            ['A', ..] => bed.Library.Folder.Id + value[1..],
            ['B', ..] => offline.Id + value[1..],
            _ => value,
        };

        public async Task<string[]> SearchAsync(IFilterDefinition definition, FilterValue value)
        {
            var filter = new SearchFilter { Conditions = [new FilterCondition(definition, value)] };
            var results = await bed.SearchAsync(new SearchQuery { Filter = filter });
            return [.. results.Hits.Select(hit => names[hit.ItemId]).Order(StringComparer.Ordinal)];
        }

        public async ValueTask InitializeAsync()
        {
            bed = await SearchTestBed.CreateAsync();
            FolderFilter = new FolderFilter(bed.Session);
            offline = IndexedFolder.Create(bed.Library.Library.Library.Id, @"E:\Phone", @"E:\Phone", LibraryTestBed.Start);
            offline.MarkOffline(LibraryTestBed.Start);
            await bed.Library.Library.Folders.AddAsync(offline, Token);

            var online = bed.Library.Folder.Id;
            await AddAsync("root", online, "root.jpg", MediaItemStatus.Pending, MediaItemStatus.Indexed);
            await AddAsync("year", online, "2024/year.jpg", MediaItemStatus.Pending, MediaItemStatus.Indexed);
            await AddAsync("trip", online, "2024/Trips/trip.jpg", MediaItemStatus.Pending);

            // A sibling whose name starts with "2024", so "2024" must not match it as a prefix; still Discovered.
            await AddAsync("lookalike", online, "2024x/lookalike.jpg");
            await AddAsync("percent", online, "100%/percent.jpg", MediaItemStatus.Error);
            await AddAsync("offline-root", offline.Id, "offline-root.jpg", MediaItemStatus.Missing);
            await AddAsync("offline-sub", offline.Id, "2024/offline-sub.jpg", MediaItemStatus.Pending, MediaItemStatus.Indexed);

            // One folder on disk, its name stored in two cases (e.g. renamed by case between scans).
            await AddAsync("photo-upper", offline.Id, "Фото/a.jpg", MediaItemStatus.Pending, MediaItemStatus.Indexed);
            await AddAsync("photo-lower", offline.Id, "фото/b.jpg", MediaItemStatus.Pending, MediaItemStatus.Indexed);
        }

        public ValueTask DisposeAsync() => bed.DisposeAsync();

        private async Task AddAsync(string name, Id<IndexedFolder> folderId, string path, params MediaItemStatus[] statuses)
        {
            var item = await bed.AddItemAsync(
                names.Count + 1,
                configure: item =>
                {
                    item.Relocate(folderId, RelativePath.From(path));
                    foreach (var status in statuses)
                    {
                        item.ChangeStatus(status, LibraryTestBed.Start);
                    }
                });
            names[item.Id] = name;
        }
    }
}
