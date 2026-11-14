using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Infrastructure.DataAccess.Search.Filters;

namespace MediaFileClassifier.Infrastructure.Tests.Search;

/// <summary>
/// The tag filter (E10-S03-T05): included tags with AND or OR, excluded tags, and effective-tag semantics (user, AI of
/// the active run, technical; rejected tags and rejected or replaced runs never match), plus autocompletion.
/// </summary>
public sealed class TagsFilterTests(TagsFilterTests.Library library) : IClassFixture<TagsFilterTests.Library>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string[], string[], bool, string[]> Cases() => new()
    {
        // Included, any of them (OR) or all (AND); user and AI tags alike, ignoring case and extra white space.
        { ["beach"], [], false, ["ai", "user"] },
        { ["BEACH"], [], true, ["ai", "user"] },
        { ["  beach "], [], true, ["ai", "user"] },
        { ["beach", "sea"], [], false, ["ai", "technical", "user"] },
        { ["beach", "sea"], [], true, ["user"] },
        { ["beach", "dog"], [], true, [] },

        // Technical tags, and case in another alphabet.
        { ["ПЛЯЖ"], [], false, ["technical"] },

        // Excluded only, and with included ones.
        { [], ["beach"], false, ["rejected-run", "rejected-tag", "replaced-run", "technical", "untagged"] },
        { [], ["beach", "sea"], true, ["rejected-run", "rejected-tag", "replaced-run", "untagged"] },
        { ["sea"], ["beach"], false, ["technical"] },
        { ["beach", "sea"], ["sunset"], false, ["technical", "user"] },

        // Not effective: a tag the user rejected, the tags of a rejected run and of a run a newer one replaced.
        { ["dog"], [], false, ["rejected-tag"] },
        { ["mountain"], [], false, [] },
        { ["forest"], [], false, [] },
        { ["lake"], [], false, ["replaced-run"] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Filter_CombinesIncludedAndExcludedEffectiveTags(string[] include, string[] exclude, bool matchAll, string[] expected)
    {
        var names = await library.SearchAsync(new IncludeExcludeValue(include, exclude, matchAll));

        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData(null, new[] { "Beach", "Sea", "dog", "lake", "sunset", "Пляж" })]
    [InlineData("", new[] { "Beach", "Sea", "dog", "lake", "sunset", "Пляж" })]
    [InlineData("S", new[] { "Sea", "sunset" })]
    [InlineData("п", new[] { "Пляж" })]
    [InlineData("%", new string[0])]
    [InlineData("mountain", new string[0])]
    [InlineData("pr", new string[0])]
    public async Task Options_ListEffectiveTags_ByPrefix_MostUsedFirst(string? prefix, string[] expected)
    {
        var options = await library.Filter.GetOptionsAsync(prefix, Token);

        Assert.Equal(expected, options.Select(option => option.Value));
        Assert.All(options, option => Assert.Equal(option.Value, option.DisplayName));
    }

    [Fact]
    public async Task Options_AreEmpty_WithoutALibrary()
    {
        var options = await new TagsFilter(new TestSession(null)).GetOptionsAsync(null, Token);

        Assert.Empty(options);
    }

    [Fact]
    public void ToSql_PassesEveryTagAsAParameter()
    {
        var sql = new Infrastructure.DataAccess.Search.SearchSql();

        var condition = library.Filter.ToSql(new IncludeExcludeValue(["Secret"], ["Other"], matchAll: true), sql);

        Assert.NotNull(condition);
        Assert.DoesNotContain("SECRET", condition, StringComparison.Ordinal);
        Assert.Equal(["SECRET", "OTHER"], sql.Parameters.Select(parameter => parameter.Value));
    }

    /// <summary>
    /// Items tagged in every way; the cases use their names.
    /// </summary>
    public sealed class Library : IAsyncLifetime
    {
        private readonly Dictionary<Id<MediaItem>, string> names = [];

        private SearchTestBed bed = null!;

        internal TagsFilter Filter { get; private set; } = null!;

        public async Task<string[]> SearchAsync(IncludeExcludeValue value)
        {
            var filter = new SearchFilter { Conditions = [new FilterCondition(Filter, value)] };
            var results = await bed.SearchAsync(new SearchQuery { Filter = filter });
            return [.. results.Hits.Select(hit => names[hit.ItemId]).Order(StringComparer.Ordinal)];
        }

        public async ValueTask InitializeAsync()
        {
            bed = await SearchTestBed.CreateAsync();
            Filter = new TagsFilter(bed.Session);

            await AddAsync("user", metadata =>
            {
                metadata.AddTag(TagName.From("Beach"), Now());
                metadata.AddTag(TagName.From("Sea"), Now());
            });
            await AddAsync("ai", metadata => Apply(metadata, "caption", "beach", "sunset"));
            await AddAsync("rejected-tag", metadata =>
            {
                Apply(metadata, "caption", "beach", "dog");
                metadata.RejectTag(TagName.From("beach"), Now());
            });
            await AddAsync("rejected-run", metadata =>
            {
                Apply(metadata, "caption", "mountain");
                metadata.RejectActiveRun("caption", Now());
            });
            await AddAsync("replaced-run", metadata =>
            {
                Apply(metadata, "caption", "forest");
                Apply(metadata, "caption", "lake");
            });
            await AddAsync("technical", metadata => metadata.SetTechnicalTags([TagName.From("sea"), TagName.From("Пляж")], Now()));
            await AddAsync("untagged", _ => { });

            // Excluded items never reach the results, so autocompletion leaves their tags out.
            await AddAsync("excluded", metadata => metadata.AddTag(TagName.From("Private"), Now()), item => item.ChangeStatus(MediaItemStatus.Excluded, Now()));
        }

        public ValueTask DisposeAsync() => bed.DisposeAsync();

        private DateTimeOffset Now() => bed.Library.Clock.Tick();

        private void Apply(ItemMetadata metadata, string analyzer, params string[] tags)
        {
            var run = AnalysisRun.Start(metadata.ItemId, analyzer, bed.Library.Model, "p1", Now());
            run.Complete(Now());
            metadata.ApplyRun(run, [], [.. tags.Select(tag => TagAssignment.Ai(run, TagName.From(tag), 0.8, Now()))], 1);
        }

        private async Task AddAsync(string name, Action<ItemMetadata> tag, Action<MediaItem>? configure = null)
        {
            var item = await bed.AddItemAsync(names.Count + 1, configure: configure);
            await bed.Library.Library.Metadata.UpdateAsync(item.Id, tag, Token);
            names[item.Id] = name;
        }
    }
}
