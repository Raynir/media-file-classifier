using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for the FTS5 keyword index of <see cref="SearchStore"/> (E01-S04-T02): what it matches, and that every change
/// is searchable as soon as its write commits.
/// </summary>
public sealed class SearchStoreTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task EditedTitle_IsFoundByTheNextQuery_AndTheOldTitleIsNot()
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, "Sunset over the harbour");
        Assert.Equal([item.Id], await FindAsync("harbour"));

        await EditAsync(item, DescriptiveField.Title, "Morning fog");

        Assert.Equal([item.Id], await FindAsync("fog"));
        Assert.Empty(await FindAsync("harbour"));
    }

    [Fact]
    public async Task NewItem_IsFoundByItsFileNameAtOnce()
    {
        var item = await AddItemAsync(42);

        Assert.Equal([item.Id], await FindAsync("IMG_00042"));
    }

    [Theory]
    [InlineData(DescriptiveField.Title, "lighthouse")]
    [InlineData(DescriptiveField.Description, "lighthouse")]
    [InlineData(DescriptiveField.Summary, "lighthouse")]
    [InlineData(DescriptiveField.OcrText, "lighthouse")]
    [InlineData(DescriptiveField.Transcript, "lighthouse")]
    [InlineData(DescriptiveField.Notes, "lighthouse")]
    public async Task EveryTextField_IsSearchable(DescriptiveField field, string word)
    {
        var item = await AddItemAsync(1);
        await AddItemAsync(2);
        await EditAsync(item, field, $"An old {word} on the cliff");

        Assert.Equal([item.Id], await FindAsync(word));
    }

    [Fact]
    public async Task ListFieldsAndTags_AreSearchable()
    {
        var item = await AddItemAsync(1);
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Objects, FieldValue.FromList(["bicycle", "lamp post"]), bed.Clock.Tick()), Token);
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("Holiday"), Token);

        Assert.Equal([item.Id], await FindAsync("bicycle"));
        Assert.Equal([item.Id], await FindAsync("holiday"));
    }

    [Fact]
    public async Task RemovedTag_IsNoLongerFound()
    {
        var item = await AddItemAsync(1);
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("holiday"), Token);

        await bed.Library.Metadata.RemoveTagAsync([item.Id], TagName.From("holiday"), Token);

        Assert.Empty(await FindAsync("holiday"));
    }

    [Fact]
    public async Task UserValue_HidesTheAiValue()
    {
        var item = await AddItemAsync(1);
        var run = AnalysisRun.Start(item.Id, "caption", bed.Model, "p1", bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        await bed.Library.Metadata.UpdateAsync(
            item.Id,
            metadata => metadata.ApplyRun(run, [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText("A cat"), 0.9, bed.Clock.Tick())], [], 1),
            Token);
        Assert.Equal([item.Id], await FindAsync("cat"));

        await EditAsync(item, DescriptiveField.Title, "Our dog");

        Assert.Empty(await FindAsync("cat"));
        Assert.Equal([item.Id], await FindAsync("dog"));
    }

    [Theory]
    [InlineData("Café in Paris", "cafe")]
    [InlineData("cafe in Paris", "CAFÉ")]
    [InlineData("Прогулка по Москве", "москве")]
    [InlineData("Новогодняя ёлка", "елка")]
    [InlineData("Lighthouse", "light")]
    [InlineData("Прогулка", "прог")]
    public async Task Words_MatchAsPrefixes_IgnoringCaseAndDiacritics(string title, string query)
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, title);

        Assert.Equal([item.Id], await FindAsync(query));
    }

    [Fact]
    public async Task EveryWord_MustMatch_InAnyField()
    {
        var both = await AddItemAsync(1);
        var one = await AddItemAsync(2);
        await EditAsync(both, DescriptiveField.Title, "Red car");
        await EditAsync(both, DescriptiveField.OcrText, "Parking");
        await EditAsync(one, DescriptiveField.Title, "Red bus");

        Assert.Equal([both.Id], await FindAsync("red parking"));
        Assert.Equal(new[] { both.Id, one.Id }.OrderBy(id => id.Value), (await FindAsync("red")).OrderBy(id => id.Value));
    }

    [Fact]
    public async Task PathSubstring_MatchesInsideAWord()
    {
        var item = await AddItemAsync(12345);
        await AddItemAsync(1);

        Assert.Equal([item.Id], await FindAsync("2345"));
        Assert.Empty(await FindAsync("45"));
    }

    [Fact]
    public async Task MovedItem_IsFoundByItsNewPathOnly()
    {
        var item = await AddItemAsync(1);
        item.Relocate(bed.Folder.Id, RelativePath.From("trips/norway/fjord.jpg"));
        await bed.Library.Items.UpdateAsync(item, Token);

        Assert.Equal([item.Id], await FindAsync("fjord"));
        Assert.Equal([item.Id], await FindAsync("norway"));
        Assert.Empty(await FindAsync("IMG_00001"));
    }

    [Fact]
    public async Task DeletedItem_LeavesNoIndexRows()
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, "lighthouse");

        await bed.Library.Items.DeleteAsync(item.Id, Token);

        Assert.Empty(await FindAsync("lighthouse"));
        Assert.Equal(0, await CountIndexRowsAsync());
    }

    [Fact]
    public async Task Ranking_PutsTitleMatchesBeforeDescriptionMatches()
    {
        var inDescription = await AddItemAsync(1);
        var inTitle = await AddItemAsync(2);
        await EditAsync(inDescription, DescriptiveField.Description, "A lighthouse at night, waves on the rocks below");
        await EditAsync(inTitle, DescriptiveField.Title, "Lighthouse");

        Assert.Equal([inTitle.Id, inDescription.Id], await FindAsync("lighthouse"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("-")]
    [InlineData("\"lighthouse")]
    [InlineData("light*")]
    [InlineData("NEAR(a b)")]
    [InlineData("title:x")]
    public async Task QuerySyntaxCharacters_AreReadAsText(string query)
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, "lighthouse");

        var exception = await Record.ExceptionAsync(() => FindAsync(query));

        Assert.Null(exception);
    }

    [Fact]
    public async Task MatchedFields_NameTheFieldsWithAnyTerm()
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, "Lighthouse");
        await EditAsync(item, DescriptiveField.OcrText, "Keep out");
        var document = Assert.Single((await bed.Library.Search.FindKeywordMatchesAsync(Search("light"), Token)).Matches).DocumentId;

        var fields = await bed.Library.Search.GetMatchedFieldsAsync([document], ["light", "keep", "00001"], Token);

        Assert.Equal(SearchFields.Title | SearchFields.OcrText | SearchFields.FileName, fields[document]);
    }

    [Theory]
    [InlineData("l")]
    [InlineData("k")]
    public async Task MatchedFields_OneLetterPrefix_NamesTheFields(string term)
    {
        var item = await AddItemAsync(1);
        await EditAsync(item, DescriptiveField.Title, "Lighthouse");
        await EditAsync(item, DescriptiveField.OcrText, "Keep out");
        var document = Assert.Single((await bed.Library.Search.FindKeywordMatchesAsync(Search(term), Token)).Matches).DocumentId;

        var fields = await bed.Library.Search.GetMatchedFieldsAsync([document], [term], Token);

        Assert.Equal(term == "l" ? SearchFields.Title : SearchFields.OcrText, fields[document]);
    }

    [Fact]
    public async Task TopMatches_FullPage_IsThePageOfTheFullQuery()
    {
        for (var i = 1; i <= 12; i++)
        {
            var item = await AddItemAsync(i);
            await EditAsync(item, DescriptiveField.Title, string.Join(' ', Enumerable.Repeat("lake", i % 4 + 1)) + " shore");
        }

        var full = await bed.Library.Search.FindKeywordMatchesAsync(Search("la") with { Limit = 5 }, Token);
        var top = await bed.Library.Search.FindKeywordMatchesAsync(Search("la") with { Limit = 5, NeedsTotal = false }, Token);

        Assert.Equal(full.Matches, top.Matches);
        Assert.Equal(12, full.TotalCount);
        Assert.Equal(5, top.TotalCount);
    }

    [Fact]
    public async Task TopMatches_ShortPage_FallsBackToEveryMatch_WithPathSubstringHitsLast()
    {
        var titled = await AddItemAsync(1);
        await EditAsync(titled, DescriptiveField.Title, "IMG 0001 copy");
        var byPath = await AddItemAsync(2);
        await EditAsync(byPath, DescriptiveField.Title, "unrelated");

        // "0001" is a word of the first item's title, and only a path substring of IMG_00010.
        var pathOnly = await AddItemAsync(10);
        var top = await bed.Library.Search.FindKeywordMatchesAsync(Search("0001") with { NeedsTotal = false }, Token);

        Assert.Equal([titled.Id, pathOnly.Id], top.Matches.Select(match => match.ItemId));
        Assert.Equal(2, top.TotalCount);
        Assert.Equal((await bed.Library.Search.FindKeywordMatchesAsync(Search("0001"), Token)).Matches, top.Matches);
    }

    [Fact]
    public async Task TopMatches_AppliesTheFilter()
    {
        var videos = new List<Id<MediaItem>>();
        for (var i = 1; i <= 6; i++)
        {
            var item = i % 2 == 0 ? await AddItemAsync(i, MediaType.Video) : await AddItemAsync(i);
            await EditAsync(item, DescriptiveField.Title, "harbour");
            if (i % 2 == 0)
            {
                videos.Add(item.Id);
            }
        }

        var filter = new SearchFilter { MediaTypes = [MediaType.Video] };
        var top = await bed.Library.Search.FindKeywordMatchesAsync(Search("h", filter) with { Limit = 2, NeedsTotal = false }, Token);
        var full = await bed.Library.Search.FindKeywordMatchesAsync(Search("h", filter) with { Limit = 2 }, Token);

        Assert.Equal(full.Matches, top.Matches);
        Assert.All(top.Matches, match => Assert.Contains(match.ItemId, videos));
    }

    [Fact]
    public async Task Paging_ReportsTheTotalOnEveryPage()
    {
        for (var i = 1; i <= 5; i++)
        {
            await AddItemAsync(i);
        }

        var page = await bed.Library.Search.FindKeywordMatchesAsync(Search("img") with { Offset = 2, Limit = 2 }, Token);
        var past = await bed.Library.Search.FindKeywordMatchesAsync(Search("img") with { Offset = 10, Limit = 2 }, Token);

        Assert.Equal(2, page.Matches.Count);
        Assert.Equal(5, page.TotalCount);
        Assert.Empty(past.Matches);
        Assert.Equal(5, past.TotalCount);
    }

    [Fact]
    public async Task NewLibrary_NeedsNoRebuild()
    {
        Assert.False(await bed.Library.Search.IsRebuildNeededAsync(Token));
    }

    [Fact]
    public async Task UpgradeFromVersion5_IndexesExistingItemsByPath_AndMarksARebuild()
    {
        var item = await AddItemAsync(7);
        await EditAsync(item, DescriptiveField.Title, "lighthouse");
        await bed.Library.DisposeAsync();
        using (var probe = new DesignTimeLibraryDbContextFactory().CreateDbContext([]))
        {
            var beforeSearch = probe.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_SearchIndex", StringComparison.Ordinal)).Last();
            await using var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
            await db.GetService<IMigrator>().MigrateAsync(beforeSearch, cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.True(await bed.Library.Search.IsRebuildNeededAsync(Token));
        Assert.Equal([item.Id], await FindAsync("IMG_00007"));
        Assert.Empty(await FindAsync("lighthouse"));
    }

    [Fact]
    public async Task UpgradeFromVersion10_KeepsTheIndexedText_AndAddsOneLetterPrefixes()
    {
        var item = await AddItemAsync(7);
        await EditAsync(item, DescriptiveField.Title, "lighthouse");
        await bed.Library.DisposeAsync();
        using (var probe = new DesignTimeLibraryDbContextFactory().CreateDbContext([]))
        {
            var beforePrefixes = probe.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_SearchPrefixIndex", StringComparison.Ordinal)).Last();
            await using var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
            await db.GetService<IMigrator>().MigrateAsync(beforePrefixes, cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        Assert.Contains("prefix = '2 3'", await ReadTextTableSqlAsync(), StringComparison.Ordinal);
        await bed.OpenAgainAsync();

        Assert.Contains("prefix = '1 2 3'", await ReadTextTableSqlAsync(), StringComparison.Ordinal);
        Assert.False(await bed.Library.Search.IsRebuildNeededAsync(Token));
        Assert.Equal([item.Id], await FindAsync("lighthouse"));
        Assert.Equal([item.Id], await FindAsync("l"));
        var added = await AddItemAsync(8);
        await EditAsync(added, DescriptiveField.Title, "harbour");
        Assert.Equal([added.Id], await FindAsync("h"));
    }

    [Fact]
    public async Task UpgradeFromVersion11_FillsTheSortKeys()
    {
        var first = await AddItemAsync(10);
        var second = await AddItemAsync(9);
        await bed.Library.DisposeAsync();
        using (var probe = new DesignTimeLibraryDbContextFactory().CreateDbContext([]))
        {
            var beforeSortKeys = probe.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_SearchSortKeys", StringComparison.Ordinal)).Last();
            await using var db = await new LibraryConnectionFactory(bed.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
            await db.GetService<IMigrator>().MigrateAsync(beforeSortKeys, cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.OpenAgainAsync();

        Assert.False(await bed.Library.Search.IsRebuildNeededAsync(Token));
        Assert.Equal([second.Id, first.Id], await ListAsync(SortMode.Name));
        Assert.Equal([first.Id, second.Id], await ListAsync(SortMode.Size));
        var added = await AddItemAsync(1);
        Assert.Equal([added.Id, second.Id, first.Id], await ListAsync(SortMode.Name));
    }

    [Fact]
    public async Task List_ReportsTheTotalOnEveryPage()
    {
        for (var i = 1; i <= 5; i++)
        {
            await AddItemAsync(i);
        }

        var first = await bed.Library.Search.ListAsync(SearchFilter.None, SortMode.Name, false, 0, 2, cancellationToken: Token);
        var last = await bed.Library.Search.ListAsync(SearchFilter.None, SortMode.Name, false, 4, 2, cancellationToken: Token);
        var past = await bed.Library.Search.ListAsync(SearchFilter.None, SortMode.Name, false, 10, 2, cancellationToken: Token);
        var whole = await bed.Library.Search.ListAsync(SearchFilter.None, SortMode.Name, false, 0, 10, cancellationToken: Token);

        Assert.Equal((2, 5), (first.Documents.Count, first.TotalCount));
        Assert.Equal((1, 5), (last.Documents.Count, last.TotalCount));
        Assert.Equal((0, 5), (past.Documents.Count, past.TotalCount));
        Assert.Equal((5, 5), (whole.Documents.Count, whole.TotalCount));
    }

    internal static KeywordSearch Search(string text, SearchFilter? filter = null) =>
        new(text.Split(' ', StringSplitOptions.RemoveEmptyEntries), filter ?? SearchFilter.None, KeywordFieldWeights.Default, SortMode.Relevance, false, 0, 100);

    private async Task<MediaItem> AddItemAsync(int number, MediaType type = MediaType.Image)
    {
        var item = type == MediaType.Image
            ? bed.NewItem(number)
            : MediaItem.Discover(bed.Folder.Id, RelativePath.From($"album/VID_{number:D5}.mp4"), type, 1000 + number, bed.Clock.Tick(), bed.Clock.Tick(), (UInt128)number * 104729, bed.Clock.Tick());
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private Task EditAsync(MediaItem item, DescriptiveField field, string text) =>
        bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(field, FieldValue.FromText(text), bed.Clock.Tick()), Token);

    private async Task<IReadOnlyList<Id<MediaItem>>> ListAsync(SortMode sort) =>
        [.. (await bed.Library.Search.ListAsync(SearchFilter.None, sort, false, 0, 100, cancellationToken: Token)).Documents.Select(document => document.ItemId)];

    private async Task<IReadOnlyList<Id<MediaItem>>> FindAsync(string text) =>
        [.. (await bed.Library.Search.FindKeywordMatchesAsync(Search(text), Token)).Matches.Select(match => match.ItemId)];

    private async Task<string> ReadTextTableSqlAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={bed.Temporary.DatabasePath};Pooling=False");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "sql" FROM "sqlite_master" WHERE "name" = 'search_text'""";
        return (string)(await command.ExecuteScalarAsync(Token))!;
    }

    private async Task<long> CountIndexRowsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={bed.Library.Layout.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT count(*) FROM "search_document") + (SELECT count(*) FROM "search_text") + (SELECT count(*) FROM "search_filename")
            """;
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }
}
