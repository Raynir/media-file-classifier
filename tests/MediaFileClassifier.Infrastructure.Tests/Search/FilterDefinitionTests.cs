using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess;
using MediaFileClassifier.Infrastructure.DataAccess.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;
using MediaFileClassifier.Infrastructure.Tests.Media;
using MediaFileClassifier.UseCases.Search.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The filter definition interface (E10-S01-T01): the reference filters, their registration and the SQL builder they
/// write through. <see cref="SearchFilterTests"/> runs them on every search path.
/// </summary>
public sealed class FilterDefinitionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void AddDataAccess_RegistersTheReferenceFilters()
    {
        using var services = FormatTestSupport.CreateServices();
        var collection = new ServiceCollection();
        collection.AddSingleton(services.GetRequiredService<Abstractions.Formats.IMediaFormatRegistry>());
        collection.AddSingleton<Abstractions.Libraries.ILibrarySession>(new TestSession(null));
        collection.AddDataAccess();
        using var provider = collection.BuildServiceProvider();

        var registry = new FilterRegistry(provider.GetServices<IFilterDefinition>());

        Assert.Equal(
            [
                (MediaTypeFilter.FilterId, FilterValueKind.MultiSelect, typeof(MultiSelectValue)),
                (FileFormatFilter.FilterId, FilterValueKind.MultiSelect, typeof(MultiSelectValue)),
                (DateFilter.FilterId, FilterValueKind.DateRange, typeof(DateRangeValue)),
                (ResolutionFilter.FilterId, FilterValueKind.Resolution, typeof(ResolutionValue)),
                (StatusFilter.FilterId, FilterValueKind.MultiSelect, typeof(MultiSelectValue)),
                (OutdatedAnalysisFilter.FilterId, FilterValueKind.Boolean, typeof(BooleanValue)),
                (NewSuggestionsFilter.FilterId, FilterValueKind.Boolean, typeof(BooleanValue)),
                (FolderFilter.FilterId, FilterValueKind.Folder, typeof(FolderValue)),
                (TagsFilter.FilterId, FilterValueKind.IncludeExclude, typeof(IncludeExcludeValue)),
            ],
            registry.All.Select(filter => (filter.Id, filter.Kind, filter.ValueType)));

        // The outdated filter needs the current provenance, which the use cases provide.
        Assert.Equal([OutdatedAnalysisFilter.FilterId], registry.All.Where(filter => !filter.IsAvailable).Select(filter => filter.Id));
    }

    [Fact]
    public async Task MediaTypeFilter_OffersEveryMediaType()
    {
        var options = await new MediaTypeFilter().GetOptionsAsync(null, Token);

        Assert.Equal(Enum.GetNames<MediaType>(), options.Select(option => option.Value));
        Assert.Equal("Filter_MediaType_Video", options.Single(option => option.Value == "Video").DisplayNameKey);
    }

    [Fact]
    public async Task FileFormatFilter_OffersTheRegisteredFormats()
    {
        var registry = FormatTestSupport.CreateRegistry();

        var options = await new FileFormatFilter(registry).GetOptionsAsync(null, Token);

        Assert.Equal(registry.Handlers.Select(handler => handler.FormatId), options.Select(option => option.Value));
        Assert.Contains(options, option => option is { Value: "jpeg", DisplayName: "JPEG" });
    }

    [Fact]
    public void ToSql_EmptyValue_FiltersNothing()
    {
        var sql = new SearchSql();

        Assert.Null(new MediaTypeFilter().ToSql(new MultiSelectValue([]), sql));
        Assert.Empty(sql.Parameters);
        Assert.False(sql.NeedsMediaItem);
    }

    [Fact]
    public void ToSql_ValueOfAnotherType_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MediaTypeFilter().ToSql(new BooleanValue(true), new SearchSql()));

        Assert.Contains(MediaTypeFilter.FilterId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSql_JoinsWhatTheConditionReads_AndPassesValuesAsParameters()
    {
        var sql = new SearchSql();
        IFilterSql builder = sql;

        var condition = builder.Like(builder.MediaItem("path"), "%." + builder.EscapeLike("j_g"));
        var date = builder.ItemDate;

        Assert.Equal("""m."path" LIKE $p0 ESCAPE '\'""", condition);
        Assert.Equal("%.j\\_g", sql.Parameters[0].Value);
        Assert.Contains("t.\"date_taken\"", date, StringComparison.Ordinal);
        Assert.True(sql.NeedsMediaItem);
        Assert.True(sql.NeedsTechnicalMetadata);
        Assert.Equal("""t."width" """.Trim(), builder.TechnicalMetadata("width"));
        Assert.Equal("""d."item_id" """.Trim(), builder.ItemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("path\"; DROP TABLE media_item; --")]
    [InlineData("Path")]
    public void ColumnNames_OnlyPlainIdentifiersPass(string column)
    {
        IFilterSql sql = new SearchSql();

        Assert.Throws<ArgumentException>(() => sql.MediaItem(column));
        Assert.Throws<ArgumentException>(() => sql.TechnicalMetadata(column));
    }
}
