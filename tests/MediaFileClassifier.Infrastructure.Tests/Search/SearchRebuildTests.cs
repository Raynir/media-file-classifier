using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// Tests for rebuilding the search indexes from the source tables (E01-S04-T06).
/// </summary>
public sealed class SearchRebuildTests : IAsyncLifetime
{
    private static readonly string[] Words = ["harbour", "sunset", "forest", "lighthouse", "кот", "москва", "café", "bridge"];

    private SearchTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await SearchTestBed.CreateAsync();
        var random = new Random(3);
        for (var i = 1; i <= 60; i++)
        {
            var item = await bed.AddItemAsync(
                i,
                title: $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]}",
                vector: [(float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 0.1f]);
            if (i % 4 == 0)
            {
                await bed.Library.Library.Metadata.AddTagAsync([item.Id], TagName.From(Words[random.Next(Words.Length)]), Token);
            }

            if (i % 5 == 0)
            {
                await bed.Library.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.OcrText, FieldValue.FromText("Exit 42"), bed.Library.Clock.Tick()), Token);
            }
        }
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Theory]
    [InlineData("DROP TABLE \"search_text\"; DROP TABLE \"search_filename\"; DROP TABLE \"search_document\"; DROP TABLE \"search_state\";")]
    [InlineData("DELETE FROM \"search_text\"; DELETE FROM \"search_filename\"; DELETE FROM \"search_document\";")]
    [InlineData("DELETE FROM \"search_text\" WHERE \"rowid\" % 2 = 0; UPDATE \"search_text\" SET \"title\" = 'garbage';")]
    [InlineData("UPDATE \"search_document\" SET \"excluded\" = 1 - \"excluded\";")]
    public async Task DeletedIndex_RebuildsToIdenticalResults(string damage)
    {
        var before = await RunQueriesAsync();

        await ExecuteAsync(damage);
        await bed.Index.RebuildAsync(cancellationToken: Token);

        Assert.Equal(before, await RunQueriesAsync());
    }

    [Fact]
    public async Task StaleExcludedFlag_IsRepairedByRebuild()
    {
        await ExecuteAsync("""UPDATE "media_item" SET "status" = 5 WHERE "rowid" % 3 = 0""");
        var before = await RunQueriesAsync();

        await ExecuteAsync("""UPDATE "search_document" SET "excluded" = 0""");
        await bed.Index.RebuildAsync(cancellationToken: Token);

        Assert.Equal(before, await RunQueriesAsync());
    }

    [Fact]
    public async Task DroppedIndex_IsRecreatedAsTheMigrationCreatedIt()
    {
        var before = await ReadSchemaAsync();

        await ExecuteAsync("""
            DROP TRIGGER "search_item_insert"; DROP TRIGGER "search_item_path"; DROP TRIGGER "search_item_delete";
            DROP TABLE "search_text"; DROP TABLE "search_filename"; DROP TABLE "search_document"; DROP TABLE "search_state";
            """);
        await bed.Index.RebuildAsync(cancellationToken: Token);

        Assert.Equal(before, await ReadSchemaAsync());
        var added = await bed.AddItemAsync(500, title: "zeppelin");
        Assert.Equal([added.Id], (await bed.SearchAsync(new SearchQuery { Text = "zeppelin" })).Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Rebuild_ReportsProgressUpToOne()
    {
        var reports = new List<double>();

        await bed.Index.RebuildAsync(new SynchronousProgress(reports.Add), Token);

        Assert.NotEmpty(reports);
        Assert.Equal(1, reports[^1]);
        Assert.Equal(reports.Order(), reports);
    }

    [Fact]
    public async Task UpgradedLibrary_IsRebuiltInTheBackgroundWhenOpened()
    {
        var item = await bed.AddItemAsync(100, title: "zeppelin");
        await bed.Session.SetCurrentAsync(null);
        await bed.Library.Library.DisposeAsync();
        using (var probe = new DesignTimeLibraryDbContextFactory().CreateDbContext([]))
        {
            var beforeSearch = probe.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_SearchIndex", StringComparison.Ordinal)).Last();
            await using var db = await new LibraryConnectionFactory(bed.Library.Temporary.DatabasePath).CreateContextAsync(cancellationToken: Token);
            await db.GetService<IMigrator>().MigrateAsync(beforeSearch, cancellationToken: Token);
        }

        SqliteConnection.ClearAllPools();
        await bed.Library.OpenAgainAsync();
        await bed.Session.SetCurrentAsync(bed.Library.Library);
        await bed.Index.Ready;

        Assert.False(await bed.Library.Library.Search.IsRebuildNeededAsync(Token));
        Assert.Equal([item.Id], (await bed.SearchAsync(new SearchQuery { Text = "zeppelin" })).Hits.Select(hit => hit.ItemId));
    }

    [Fact]
    public async Task Rebuild_WithoutLibrary_DoesNothing()
    {
        using var index = new SearchIndex(new TestSession(null), NullLogger<SearchIndex>.Instance);

        await index.RebuildAsync(cancellationToken: Token);

        Assert.Equal(VectorIndexState.Unavailable, index.VectorState);
    }

    private async Task<List<string>> RunQueriesAsync()
    {
        var queries = new List<SearchQuery>
        {
            new() { Text = "harbour" },
            new() { Text = "sun" },
            new() { Text = "кот москва" },
            new() { Text = "cafe" },
            new() { Text = "exit 42" },
            new() { Text = "IMG_0001" },
            new() { Text = "0003" },
            new() { Vector = bed.Vector(1, 0.2f, 0.1f, 0.1f) },
            new() { Text = "forest", Vector = bed.Vector(0.1f, 1, 0.3f, 0.1f) },
            new() { Text = "bridge", Vector = bed.Vector(0.5f, 0.5f, 0.5f, 0.1f), Filter = new SearchFilter { Formats = ["jpg"] } },
            new() { Text = "lighthouse", Sort = Domain.Searches.SortMode.Name },
        };

        var lines = new List<string>();
        foreach (var query in queries)
        {
            var results = await bed.SearchAsync(query);
            lines.Add($"{query.Text}|{results.TotalCount}|" + string.Join(
                ",",
                results.Hits.Select(hit => $"{hit.ItemId}:{hit.Score:F6}:{hit.KeywordRank}:{hit.Similarity:F4}:{hit.MatchedFields}")));
        }

        return lines;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "Test SQL from constants.")]
    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={bed.Library.Library.Layout.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Token);
    }

    private async Task<List<string>> ReadSchemaAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={bed.Library.Library.Layout.DatabasePath}");
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "type" || ' ' || "name" || ': ' || coalesce("sql", '') FROM "sqlite_master"
            WHERE "name" LIKE 'search%' AND "type" IN ('table', 'trigger', 'index') ORDER BY "name"
            """;
        var schema = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token))
        {
            schema.Add(reader.GetString(0));
        }

        return schema;
    }

    /// <summary>
    /// Reports on the calling thread, unlike <see cref="Progress{T}"/>.
    /// </summary>
    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
