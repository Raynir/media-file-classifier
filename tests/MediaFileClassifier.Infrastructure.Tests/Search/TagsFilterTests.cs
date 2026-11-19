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
        // Included, any of them (OR) or all (AND); user, AI and technical tags alike (the technical "Пляж" has the form
        // "beach"), ignoring case and extra white space.
        { ["beach"], [], false, ["ai", "technical", "user"] },
        { ["BEACH"], [], true, ["ai", "technical", "user"] },
        { ["  beach "], [], true, ["ai", "technical", "user"] },
        { ["beach", "sea"], [], false, ["ai", "technical", "user"] },
        { ["beach", "sea"], [], true, ["technical", "user"] },
        { ["beach", "dog"], [], true, [] },

        // A tag matches by its other-language form too (E06-S04, D348), and case in another alphabet.
        { ["ПЛЯЖ"], [], false, ["ai", "technical", "user"] },
        { ["пляж", "beach"], [], true, ["ai", "technical", "user"] },
        { ["море", "пляж"], [], true, ["technical", "user"] },
        { ["giraffe"], [], false, ["giraffe-ai", "giraffe-ru"] },
        { ["жираф"], [], false, ["giraffe-ai", "giraffe-ru"] },
        { ["Giraffe", "Жираф"], [], true, ["giraffe-ai", "giraffe-ru"] },
        { ["жираф", "beach"], [], true, [] },
        { ["zebra"], [], false, [] },

        // Excluded only, and with included ones; an excluded tag leaves out the items that have it in either language.
        { [], ["beach"], false, ["giraffe-ai", "giraffe-ru", "rejected-run", "rejected-tag", "replaced-run", "untagged", "zebra-rejected"] },
        { [], ["beach", "sea"], true, ["giraffe-ai", "giraffe-ru", "rejected-run", "rejected-tag", "replaced-run", "untagged", "zebra-rejected"] },
        { [], ["Жираф"], false, ["ai", "rejected-run", "rejected-tag", "replaced-run", "technical", "untagged", "user", "zebra-rejected"] },
        { ["sea"], ["beach"], false, [] },
        { ["sea"], ["dog"], false, ["technical", "user"] },
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
    [InlineData(null, new[] { "Beach", "Пляж", "giraffe", "Sea", "Жираф", "море", "dog", "lake", "sunset", "закат", "озеро", "собака" })]
    [InlineData("", new[] { "Beach", "Пляж", "giraffe", "Sea", "Жираф", "море", "dog", "lake", "sunset", "закат", "озеро", "собака" })]
    [InlineData("S", new[] { "Sea", "sunset" })]
    [InlineData("п", new[] { "Пляж" })]

    // Both forms are offered, with the item counts the filter then gives: "море" is the form of "Sea" and "sea".
    [InlineData("мор", new[] { "море" })]
    [InlineData("со", new[] { "собака" })]
    [InlineData("g", new[] { "giraffe" })]
    [InlineData("ж", new[] { "Жираф" })]
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
    public async Task Options_CountTheItemsTheFilterThenFinds_InEitherLanguage()
    {
        var offered = await library.EffectiveTagsAsync();

        Assert.Contains(offered, tag => tag.Tag.Value == "море");
        Assert.Contains(offered, tag => tag.Tag.Value == "giraffe");
        foreach (var (tag, count) in offered)
        {
            var found = await library.SearchAsync(new IncludeExcludeValue([tag.Value], [], matchAll: false));
            Assert.True(count == found.Length, $"{tag.Value}: offered {count}, found {found.Length}");
        }
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

        public async Task<IReadOnlyList<(TagName Tag, int ItemCount)>> EffectiveTagsAsync() =>
            await bed.Library.Library.Metadata.GetEffectiveTagsAsync(null, 100, Token);

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

            // Tags in the dictionary have a form in the other language: "giraffe" has "жираф", and the user's "Жираф" has "giraffe".
            await AddAsync("giraffe-ai", metadata => Apply(metadata, "caption", "giraffe"));
            await AddAsync("giraffe-ru", metadata => metadata.AddTag(TagName.From("Жираф"), Now()));
            await AddAsync("zebra-rejected", metadata =>
            {
                Apply(metadata, "caption", "zebra");
                metadata.RejectTag(TagName.From("zebra"), Now());
            });

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
