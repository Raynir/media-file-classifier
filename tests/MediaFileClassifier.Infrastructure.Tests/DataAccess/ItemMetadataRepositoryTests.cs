using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="ItemMetadataRepository"/>: metadata updates that resolve effective values, and tag operations,
/// against a real library database (E01-S03-T06). Every assertion reads back from the database.
/// </summary>
public sealed class ItemMetadataRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;
    private MediaItem item = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task GetAsync_ItemWithoutMetadata_IsEmpty()
    {
        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);

        Assert.Equal(item.Id, metadata.ItemId);
        Assert.Empty(metadata.GetEffectiveValues());
        Assert.Empty(metadata.GetEffectiveTags());
    }

    [Fact]
    public async Task UpdateAsync_UnknownItem_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => bed.Library.Metadata.UpdateAsync(Id<MediaItem>.New(), metadata => metadata.ClearUserValue(DescriptiveField.Title), Token));
    }

    [Fact]
    public async Task UpdateAsync_ResolvesUserThenAiThenTechnical()
    {
        await bed.Library.Metadata.UpdateAsync(
            item.Id,
            metadata => metadata.SetTechnicalValues([new TechnicalFieldValue(item.Id, DescriptiveField.Title, FieldValue.FromText("IMG_0001"), bed.Clock.Tick())]),
            Token);
        Assert.Equal(MetadataSource.Technical, await EffectiveSourceAsync(DescriptiveField.Title));

        await ApplyRunAsync("Dog on a beach", ["dog", "beach"]);
        Assert.Equal(MetadataSource.AI, await EffectiveSourceAsync(DescriptiveField.Title));

        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), bed.Clock.Tick()), Token);
        var reloaded = await bed.Library.Metadata.GetAsync(item.Id, Token);
        var title = reloaded.GetEffectiveValue(DescriptiveField.Title);
        Assert.Equal(MetadataSource.User, title?.Source);
        Assert.Equal(UserValueOrigin.Edited, title?.Origin);
        Assert.Equal("Rex", title?.Value.Text);
    }

    [Fact]
    public async Task ApplyRun_StoresValuesTagsAndReturnsResult()
    {
        var (run, application) = await ApplyRunAsync("Dog on a beach", ["dog", "beach"]);

        Assert.True(application.EffectiveValuesChanged);
        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Equal(run.Id, metadata.GetActiveRun("caption")?.Id);
        Assert.Equal(["Dog on a beach", "car, sky"], metadata.GetEffectiveValues().Select(value => value.Value.Text));
        Assert.Equal(["beach", "dog"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value).Order());
    }

    [Fact]
    public async Task Reanalysis_FlagsSuggestionOnUserValue_AndAcceptingItStoresRunSnapshot()
    {
        await ApplyRunAsync("Dog on a beach", []);
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), bed.Clock.Tick()), Token);

        var (second, _) = await ApplyRunAsync("Dog running on a beach", []);

        var flagged = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Equal(second.Id, flagged.GetEffectiveValue(DescriptiveField.Title)?.SuggestionRunId);

        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AcceptSuggestion(DescriptiveField.Title, bed.Clock.Tick()), Token);

        var accepted = Assert.Single((await bed.Library.Metadata.GetAsync(item.Id, Token)).UserValues, value => value.Field == DescriptiveField.Title);
        Assert.Equal(UserValueOrigin.Accepted, accepted.Origin);
        Assert.Equal(second.ToReference(), accepted.AcceptedFrom);
        Assert.Equal("Dog running on a beach", accepted.Value.Text);
        Assert.Null(accepted.SuggestionRunId);
    }

    [Fact]
    public async Task Reanalysis_PrunesRunsBeyondRetention()
    {
        var (first, _) = await ApplyRunAsync("one", ["a"]);
        var (second, _) = await ApplyRunAsync("two", ["b"]);

        var (third, application) = await ApplyRunAsync("three", ["c"]);

        Assert.Equal([first.Id], application.PrunedRunIds);
        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Equal([third.Id, second.Id], metadata.Runs.OrderByDescending(run => run.StartedAt).Select(run => run.Id));
        Assert.Empty(metadata.GetRunValues(first.Id));
        Assert.Empty(metadata.GetRunTags(first.Id));
        Assert.False(metadata.Runs.Single(run => run.Id == second.Id).IsActive);
    }

    [Fact]
    public async Task UpdateAsync_ChangeThrows_StoresNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => bed.Library.Metadata.UpdateAsync(
            item.Id,
            metadata =>
            {
                metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), bed.Clock.Tick());
                metadata.AcceptAiValue(DescriptiveField.Summary, bed.Clock.Tick());
            },
            Token));

        Assert.Empty((await bed.Library.Metadata.GetAsync(item.Id, Token)).UserValues);
    }

    [Fact]
    public async Task RemoveTagAsync_LeavesTombstoneThatReanalysisRespects()
    {
        await ApplyRunAsync("Dog on a beach", ["dog", "beach"]);

        await bed.Library.Metadata.RemoveTagAsync([item.Id], TagName.From("Beach"), Token);
        await ApplyRunAsync("Dog on a beach again", ["dog", "beach"]);

        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Equal(["dog"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
        var rejection = Assert.Single(metadata.Rejections);
        Assert.Equal("beach", rejection.Tag.Value, ignoreCase: true);
        Assert.NotNull(rejection.RejectedFrom);
    }

    [Fact]
    public async Task AddTagAsync_TagsManyItemsAndRemovesTombstones()
    {
        var other = bed.NewItem(2);
        await bed.Library.Items.AddAsync(other, Token);
        await bed.Library.Metadata.RemoveTagAsync([item.Id], TagName.From("trip"), Token);

        await bed.Library.Metadata.AddTagAsync([item.Id, other.Id], TagName.From("Trip"), Token);
        await bed.Library.Metadata.AddTagAsync([other.Id], TagName.From("family"), Token);

        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Empty(metadata.Rejections);
        var tag = Assert.Single(metadata.GetEffectiveTags());
        Assert.Equal(MetadataSource.User, tag.Source);
        Assert.Equal(UserValueOrigin.Edited, tag.Origin);
        var userTags = await bed.Library.Metadata.GetUserTagsAsync(Token);
        Assert.Equal([("Trip", 2), ("family", 1)], userTags.Select(entry => (entry.Tag.Value, entry.ItemCount)));
    }

    [Fact]
    public async Task AddTagAsync_MoreItemsThanOneBatch_TagsEveryItem()
    {
        var items = Enumerable.Range(2, MediaItemRepository.BatchSize + 50).Select(bed.NewItem).ToList();
        await bed.Library.Items.AddRangeAsync(items, Token);

        await bed.Library.Metadata.AddTagAsync([item.Id, .. items.Select(other => other.Id)], TagName.From("trip"), Token);

        var tag = Assert.Single(await bed.Library.Metadata.GetUserTagsAsync(Token));
        Assert.Equal(items.Count + 1, tag.ItemCount);
    }

    [Fact]
    public async Task GetUserTagsAsync_CountsTagsIgnoringCase()
    {
        var other = bed.NewItem(2);
        await bed.Library.Items.AddAsync(other, Token);

        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("Beach"), Token);
        await bed.Library.Metadata.AddTagAsync([other.Id], TagName.From("beach"), Token);

        var tag = Assert.Single(await bed.Library.Metadata.GetUserTagsAsync(Token));
        Assert.Equal(2, tag.ItemCount);
    }

    [Theory]
    [InlineData("Отпуск", "отпуск")]
    [InlineData("Été", "été")]
    public async Task GetUserTagsAsync_CountsNonAsciiTagsIgnoringCase(string first, string second)
    {
        var other = bed.NewItem(2);
        await bed.Library.Items.AddAsync(other, Token);

        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From(first), Token);
        await bed.Library.Metadata.AddTagAsync([other.Id], TagName.From(second), Token);

        var tag = Assert.Single(await bed.Library.Metadata.GetUserTagsAsync(Token));
        Assert.Equal(TagName.From(first), tag.Tag);
        Assert.Equal(2, tag.ItemCount);
    }

    [Fact]
    public async Task RemoveTagAsync_NonAsciiTagInOtherCase_RemovesItAndLeavesOneTombstone()
    {
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("Отпуск"), Token);

        await bed.Library.Metadata.RemoveTagAsync([item.Id], TagName.From("ОТПУСК"), Token);
        await bed.Library.Metadata.RemoveTagAsync([item.Id], TagName.From("отпуск"), Token);

        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Empty(metadata.GetEffectiveTags());
        Assert.Single(metadata.Rejections);
    }

    [Fact]
    public async Task GetUserTagsAsync_SameCount_OrdersByTag()
    {
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("zoo"), Token);
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("Apple"), Token);
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("mango"), Token);

        var tags = await bed.Library.Metadata.GetUserTagsAsync(Token);

        Assert.Equal(["Apple", "mango", "zoo"], tags.Select(tag => tag.Tag.Value));
    }

    [Fact]
    public async Task AcceptAll_MakesAiValuesAndTagsUserOwned()
    {
        var (run, _) = await ApplyRunAsync("Dog on a beach", ["dog"]);

        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AcceptAll(bed.Clock.Tick()), Token);

        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.All(metadata.GetEffectiveValues(), value => Assert.Equal(MetadataSource.User, value.Source));
        var tag = Assert.Single(metadata.GetEffectiveTags());
        Assert.Equal(MetadataSource.User, tag.Source);
        Assert.Equal(run.Id, tag.RunId);
    }

    private async Task<MetadataSource?> EffectiveSourceAsync(DescriptiveField field) =>
        (await bed.Library.Metadata.GetAsync(item.Id, Token)).GetEffectiveValue(field)?.Source;

    private async Task<(AnalysisRun Run, RunApplication Application)> ApplyRunAsync(string title, IReadOnlyList<string> tags)
    {
        var run = AnalysisRun.Start(item.Id, "caption", bed.Model, "p1", bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        var now = bed.Clock.GetUtcNow();
        AiFieldValue[] values =
        [
            AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title), 0.9, now),
            AiFieldValue.For(run, DescriptiveField.Objects, FieldValue.FromList(["car", "sky"]), null, now),
        ];
        var assignments = tags.Select(tag => TagAssignment.Ai(run, TagName.From(tag), 0.8, now)).ToList();

        var (_, application) = await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.ApplyRun(run, values, assignments, 1), Token);
        return (run, application);
    }
}
