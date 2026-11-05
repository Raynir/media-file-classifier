using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using Microsoft.Data.Sqlite;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Every filter of spec 1.7, and the navigation scope, on every search path (E01-S04-T05): the FTS path translates the
/// filter into SQL, and the vector path scores only the documents the same SQL returns.
/// </summary>
public sealed class SearchFilterTests(SearchFilterTests.Library library) : IClassFixture<SearchFilterTests.Library>
{
    /// <summary>
    /// How a filtered query reaches the items.
    /// </summary>
    public enum SearchPath
    {
        Keyword,
        Vector,
        Hybrid,
        List,
    }

    public static TheoryData<string, SearchPath> Cases()
    {
        var data = new TheoryData<string, SearchPath>();
        foreach (var name in Library.BuildFilters(default).Keys)
        {
            foreach (var path in Enum.GetValues<SearchPath>())
            {
                data.Add(name, path);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Filter_KeepsExactlyTheMatchingItems(string filterName, SearchPath path)
    {
        var (filter, expected) = library.Filters[filterName];
        var query = path switch
        {
            SearchPath.Keyword => new SearchQuery { Text = "sample", Filter = filter },
            SearchPath.Vector => new SearchQuery { Vector = library.Bed.Vector(1, 0, 0, 0), Filter = filter },
            SearchPath.Hybrid => new SearchQuery { Text = "sample", Vector = library.Bed.Vector(1, 0, 0, 0), Filter = filter },
            _ => new SearchQuery { Filter = filter },
        };

        var results = await library.Bed.SearchAsync(query);

        Assert.Equal(expected.Order(), results.Hits.Select(hit => library.Numbers[hit.ItemId]).Order());
        Assert.Equal(expected.Length, results.TotalCount);
    }

    [Theory]
    [InlineData(SortMode.Name, false, new[] { 3, 1, 2 })]
    [InlineData(SortMode.Name, true, new[] { 2, 1, 3 })]
    [InlineData(SortMode.Size, false, new[] { 1, 2, 3 })]
    [InlineData(SortMode.Size, true, new[] { 3, 2, 1 })]
    [InlineData(SortMode.Date, false, new[] { 2, 3, 1 })]
    [InlineData(SortMode.ImportDate, false, new[] { 3, 2, 1 })]
    public async Task Sort_OrdersEveryPathAlike(SortMode sort, bool reverse, int[] expected)
    {
        foreach (var query in new[]
        {
            new SearchQuery { Text = "sample", Sort = sort, ReverseSort = reverse },
            new SearchQuery { Vector = library.Bed.Vector(1, 0, 0, 0), Sort = sort, ReverseSort = reverse },
            new SearchQuery { Text = "sample", Vector = library.Bed.Vector(1, 0, 0, 0), Sort = sort, ReverseSort = reverse },
            new SearchQuery { Sort = sort, ReverseSort = reverse },
        })
        {
            var results = await library.Bed.SearchAsync(query);

            Assert.Equal(expected, results.Hits.Select(hit => library.Numbers[hit.ItemId]));
        }
    }

    /// <summary>
    /// One library for every case: three items that differ in every filtered property, all titled "sample" and with a
    /// vector, and a fourth that is Excluded and so never matches.
    /// </summary>
    public sealed class Library : IAsyncLifetime
    {
        private static readonly DateTimeOffset Start = LibraryTestBed.Start;

        public Dictionary<string, (SearchFilter Filter, int[] Expected)> Filters { get; private set; } = [];

        internal SearchTestBed Bed { get; private set; } = null!;

        internal Dictionary<Id<MediaItem>, int> Numbers { get; } = [];

        private static Id<Collection> CollectionId { get; } = Id<Collection>.New();

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        public static Dictionary<string, (SearchFilter Filter, int[] Expected)> BuildFilters(Id<IndexedFolder> folder) => new()
        {
            ["none"] = (SearchFilter.None, [1, 2, 3]),
            ["type video"] = (new SearchFilter { MediaTypes = [MediaType.Video] }, [2]),
            ["types image and animated"] = (new SearchFilter { MediaTypes = [MediaType.Image, MediaType.AnimatedImage] }, [1, 3]),
            ["format JPG, any case"] = (new SearchFilter { Formats = ["JPG"] }, [1]),
            ["formats .gif and mp4"] = (new SearchFilter { Formats = [".gif", "mp4"] }, [2, 3]),
            ["format with LIKE characters"] = (new SearchFilter { Formats = ["j_g", "%"] }, []),
            ["date from 2021"] = (new SearchFilter { DateFrom = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero) }, [2, 3]),
            ["date before 2021"] = (new SearchFilter { DateTo = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero) }, [1]),
            ["date taken offset"] = (new SearchFilter { DateFrom = new DateTimeOffset(2024, 3, 10, 9, 0, 0, TimeSpan.Zero), DateTo = new DateTimeOffset(2024, 3, 10, 11, 0, 0, TimeSpan.Zero) }, [3]),
            ["at least 1 MP"] = (new SearchFilter { MinPixels = 1_000_000 }, [1]),
            ["at most 50k pixels"] = (new SearchFilter { MaxPixels = 50_000 }, [3]),
            ["favorites"] = (new SearchFilter { IsFavorite = true }, [1]),
            ["not favorites"] = (new SearchFilter { IsFavorite = false }, [2, 3]),
            ["status pending or error"] = (new SearchFilter { Statuses = [MediaItemStatus.Pending, MediaItemStatus.Error] }, [2, 3]),
            ["status excluded asked for"] = (new SearchFilter { Statuses = [MediaItemStatus.Excluded] }, []),
            ["reviewed"] = (new SearchFilter { ReviewStatus = ReviewStatus.Reviewed }, [1]),
            ["collection"] = (new SearchFilter { CollectionId = CollectionId }, [1]),
            ["user tag, any case"] = (new SearchFilter { UserTags = [TagName.From("TRIP")] }, [1]),
            ["two user tags"] = (new SearchFilter { UserTags = [TagName.From("trip"), TagName.From("beach")] }, [1]),
            ["user tag nobody has"] = (new SearchFilter { UserTags = [TagName.From("trip"), TagName.From("winter")] }, []),
            ["folder"] = (new SearchFilter { FolderId = folder }, [1, 2, 3]),
            ["other folder"] = (new SearchFilter { FolderId = Id<IndexedFolder>.New() }, []),
            ["subfolder"] = (new SearchFilter { FolderId = folder, Subfolder = "clips" }, [2]),
            ["subfolder with backslashes"] = (new SearchFilter { Subfolder = @"\album\" }, [1, 3]),
            ["imported since"] = (new SearchFilter { ImportedSince = Start.AddHours(1) }, [3]),
            ["combined"] = (new SearchFilter { MediaTypes = [MediaType.Image, MediaType.AnimatedImage], IsFavorite = false, Statuses = [MediaItemStatus.Error] }, [3]),
        };

        public async ValueTask InitializeAsync()
        {
            Bed = await SearchTestBed.CreateAsync();
            var folder = Bed.Library.Folder.Id;
            Filters = BuildFilters(folder);

            // 1: a 12 MP photo taken in 2020, favorite, reviewed, indexed, in a collection, tagged "trip" and "beach".
            var photo = await AddAsync(1, MediaItem.Discover(folder, RelativePath.From("album/IMG_00001.jpg"), MediaType.Image, 3000, Start, Start, 1, Start), [1, 0, 0, 0]);
            photo.ChangeStatus(MediaItemStatus.Pending, Start);
            photo.ChangeStatus(MediaItemStatus.Indexed, Start);
            photo.SetFavorite(true, Start);
            photo.MarkReviewed(Start);
            await Bed.Library.Library.Items.UpdateAsync(photo, Metadata(photo, 4000, 3000, new DateTime(2020, 6, 1, 12, 0, 0), null), Token);
            await Bed.Library.Library.Metadata.AddTagAsync([photo.Id], TagName.From("trip"), Token);
            await Bed.Library.Library.Metadata.AddTagAsync([photo.Id], TagName.From("beach"), Token);

            // 2: a video with no technical metadata yet, so its date is the file's: 2026. Pending.
            var video = await AddAsync(2, MediaItem.Discover(folder, RelativePath.From("clips/VID_2.mp4"), MediaType.Video, 2000, Start, Start.AddDays(-1), 2, Start.AddMinutes(1)), [0.9f, 0.1f, 0, 0]);
            video.ChangeStatus(MediaItemStatus.Pending, Start);
            await Bed.Library.Library.Items.UpdateAsync(video, Token);

            // 3: a small GIF taken at 12:00 in UTC+2 on 2024-03-10 (10:00 UTC), imported a day later. Error.
            var gif = await AddAsync(3, MediaItem.Discover(folder, RelativePath.From("album/anim_3.gif"), MediaType.AnimatedImage, 1000, Start, Start, 3, Start.AddDays(1)), [0.8f, 0.2f, 0, 0]);
            gif.ChangeStatus(MediaItemStatus.Error, Start);
            await Bed.Library.Library.Items.UpdateAsync(gif, Metadata(gif, 100, 100, new DateTime(2024, 3, 10, 12, 0, 0), TimeSpan.FromHours(2)), Token);

            // 4: Excluded, otherwise like the photo.
            var excluded = await AddAsync(4, MediaItem.Discover(folder, RelativePath.From("album/IMG_00004.jpg"), MediaType.Image, 3000, Start, Start, 4, Start), [1, 0, 0, 0]);
            excluded.ChangeStatus(MediaItemStatus.Excluded, Start);
            excluded.SetFavorite(true, Start);
            await Bed.Library.Library.Items.UpdateAsync(excluded, Token);
            await Bed.Library.Library.Metadata.AddTagAsync([excluded.Id], TagName.From("trip"), Token);

            // Collections have no repository yet (E11); write the rows directly.
            await using var connection = new SqliteConnection($"Data Source={Bed.Library.Library.Layout.DatabasePath}");
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "collection" ("id", "name", "parent_id", "created_at") VALUES ($collection, 'Trips', NULL, 0);
                INSERT INTO "collection_membership" ("collection_id", "item_id", "added_at") VALUES ($collection, $photo, 0), ($collection, $excluded, 0);
                """;
            command.Parameters.AddWithValue("$collection", CollectionId.ToString());
            command.Parameters.AddWithValue("$photo", photo.Id.ToString());
            command.Parameters.AddWithValue("$excluded", excluded.Id.ToString());
            await command.ExecuteNonQueryAsync(Token);
        }

        public ValueTask DisposeAsync() => Bed.DisposeAsync();

        private static TechnicalMetadata Metadata(MediaItem item, int width, int height, DateTime taken, TimeSpan? offset) => new()
        {
            ItemId = item.Id,
            Width = width,
            Height = height,
            DateTaken = taken,
            DateTakenOffset = offset,
            ExtractedAt = Start,
        };

        private async Task<MediaItem> AddAsync(int number, MediaItem item, float[] vector)
        {
            await Bed.Library.Library.Items.AddAsync(item, Token);
            await Bed.Library.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("sample"), Start), Token);
            await Bed.SetVectorAsync(item, vector);
            Numbers[item.Id] = number;
            return item;
        }
    }
}
