using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;

namespace MediaFileClassifier.Infrastructure.Tests.Metadata;

/// <summary>
/// Tests for the metadata write operations (E07-S01-T02) through <see cref="EditMetadataCommand"/> against a real
/// library: each operation's effect, its keyword index update, and what a re-analysis after it does.
/// </summary>
public sealed class EditMetadataFlowTests : IAsyncLifetime
{
    private static readonly DescriptiveField Title = DescriptiveField.Title;

    private MetadataTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await MetadataTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task SetField_StoresAnEditedValue_SearchableAtOnce_AndReanalysisOnlyFlagsIt()
    {
        await bed.ApplyRunAsync("Dog on a beach", []);

        var result = await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex the terrier")));

        Assert.Equal([bed.Item.Id], result.ChangedItemIds);
        Assert.Equal([bed.Item.Id], await FoundAsync("terrier"));
        var run = await bed.ApplyRunAsync("Dog running", []);
        var title = (await bed.GetAsync()).GetEffectiveValue(Title);
        Assert.Equal("Rex the terrier", title?.Value.Text);
        Assert.Equal(UserValueOrigin.Edited, title?.Origin);
        Assert.Equal(run.Id, title?.SuggestionRunId);
        Assert.Empty(await FoundAsync("running"));
    }

    [Fact]
    public async Task SetField_TheSameValueAgain_ChangesNothing()
    {
        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));

        var result = await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));

        Assert.Empty(result.ChangedItemIds);
    }

    [Fact]
    public async Task RevertField_MakesTheAiValueEffective_AndReanalysisReplacesIt()
    {
        await bed.ApplyRunAsync("Dog on a beach", []);
        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));

        await EditAsync(new MetadataEdit.RevertField(Title));

        Assert.Equal(MetadataSource.AI, (await bed.GetAsync()).GetEffectiveValue(Title)?.Source);
        Assert.Empty(await FoundAsync("Rex"));
        await bed.ApplyRunAsync("Dog running", []);
        var title = (await bed.GetAsync()).GetEffectiveValue(Title);
        Assert.Equal("Dog running", title?.Value.Text);
        Assert.Null(title?.SuggestionRunId);
    }

    [Fact]
    public async Task ConfirmField_CopiesTheAiValueAsAccepted_AndReanalysisKeepsIt()
    {
        var first = await bed.ApplyRunAsync("Dog on a beach", []);

        await EditAsync(new MetadataEdit.ConfirmField(Title));

        var accepted = (await bed.GetAsync()).GetUserValue(Title);
        Assert.Equal(UserValueOrigin.Accepted, accepted?.Origin);
        Assert.Equal(first.ToReference(), accepted?.AcceptedFrom);
        var second = await bed.ApplyRunAsync("Dog running", []);
        var title = (await bed.GetAsync()).GetEffectiveValue(Title);
        Assert.Equal("Dog on a beach", title?.Value.Text);
        Assert.Equal(second.Id, title?.SuggestionRunId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmField_WithoutAnAiValueOrWithAUserValue_ChangesNothing(bool userValue)
    {
        if (userValue)
        {
            await bed.ApplyRunAsync("Dog", []);
            await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));
        }

        var result = await EditAsync(new MetadataEdit.ConfirmField(Title));

        Assert.Empty(result.ChangedItemIds);
    }

    [Fact]
    public async Task AcceptFieldSuggestion_TakesTheNewRunsValue_AndTheSameValueLaterIsNotFlagged()
    {
        await bed.ApplyRunAsync("Dog on a beach", []);
        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));
        var second = await bed.ApplyRunAsync("Dog running", []);

        await EditAsync(new MetadataEdit.AcceptFieldSuggestion(Title));

        var user = (await bed.GetAsync()).GetUserValue(Title);
        Assert.Equal("Dog running", user?.Value.Text);
        Assert.Equal(UserValueOrigin.Accepted, user?.Origin);
        Assert.Equal(second.Id, user?.AcceptedFrom?.RunId);
        Assert.False(user?.HasNewAiSuggestion);
        Assert.Equal([bed.Item.Id], await FoundAsync("running"));
        await bed.ApplyRunAsync("Dog running", []);
        Assert.False((await bed.GetAsync()).HasNewAiSuggestions);
    }

    [Fact]
    public async Task DismissFieldSuggestion_KeepsTheValue_UntilARunProducesADifferentOne()
    {
        await bed.ApplyRunAsync("Dog on a beach", []);
        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));
        await bed.ApplyRunAsync("Dog running", []);

        await EditAsync(new MetadataEdit.DismissFieldSuggestion(Title));

        Assert.False((await bed.GetAsync()).HasNewAiSuggestions);
        await bed.ApplyRunAsync("Dog running", []);
        Assert.False((await bed.GetAsync()).HasNewAiSuggestions);
        var third = await bed.ApplyRunAsync("Dog jumping", []);
        var title = (await bed.GetAsync()).GetEffectiveValue(Title);
        Assert.Equal("Rex", title?.Value.Text);
        Assert.Equal(third.Id, title?.SuggestionRunId);
    }

    [Fact]
    public async Task AddTag_StoresItAsTypedAndRemovesItsTombstone_AndReanalysisKeepsIt()
    {
        await bed.ApplyRunAsync("Dog", ["dog", "puppy"]);
        await EditAsync(new MetadataEdit.RejectTag(TagName.From("puppy")));

        await EditAsync(new MetadataEdit.AddTag("Puppy"), new MetadataEdit.AddTag("Rex"));

        var metadata = await bed.GetAsync();
        Assert.Empty(metadata.Rejections);
        Assert.Contains(metadata.Tags, tag => tag.Source == MetadataSource.User && tag.Tag.Value == "Rex" && tag.Origin == UserValueOrigin.Edited);
        Assert.Equal([bed.Item.Id], await FoundAsync("Rex"));
        await bed.ApplyRunAsync("Dog", ["dog"]);
        var tags = (await bed.GetAsync()).GetEffectiveTags();
        Assert.Equal(MetadataSource.User, Assert.Single(tags, tag => tag.Tag.Value == "Rex").Source);
        Assert.Equal(MetadataSource.User, Assert.Single(tags, tag => tag.Tag.Value == "Puppy").Source);
    }

    [Fact]
    public async Task RemoveTag_DeletesTheUserTagWithoutATombstone_SoTheAiTagShowsAgain()
    {
        await bed.ApplyRunAsync("Dog", ["dog"]);
        await EditAsync(new MetadataEdit.AddTag("dog"), new MetadataEdit.AddTag("Rex"));

        await EditAsync(new MetadataEdit.RemoveTag(TagName.From("dog")), new MetadataEdit.RemoveTag(TagName.From("Rex")));

        var metadata = await bed.GetAsync();
        Assert.Empty(metadata.Rejections);
        Assert.Equal(MetadataSource.AI, Assert.Single(metadata.GetEffectiveTags()).Source);
        Assert.Empty(await FoundAsync("Rex"));
        await bed.ApplyRunAsync("Dog", ["dog", "rex"]);
        Assert.Equal(["dog", "rex"], (await bed.GetAsync()).GetEffectiveTags().Select(tag => tag.Tag.Value).Order());
    }

    [Fact]
    public async Task RemoveTag_ATagTheItemDoesntHave_ChangesNothing()
    {
        var result = await EditAsync(new MetadataEdit.RemoveTag(TagName.From("cat")));

        Assert.Empty(result.ChangedItemIds);
    }

    [Fact]
    public async Task RejectTag_RemovesBothFormsFromSearch_AndReanalysisDoesntBringItBack()
    {
        await bed.ApplyRunAsync("Animal", ["dog", "cat"]);
        var form = Assert.Single((await bed.GetAsync()).GetEffectiveTags(), tag => tag.Tag.Value == "dog").OtherForm;
        Assert.NotNull(form);
        Assert.Equal([bed.Item.Id], await FoundAsync(form));

        await EditAsync(new MetadataEdit.RejectTag(TagName.From("dog")));

        Assert.Empty(await FoundAsync("dog"));
        Assert.Empty(await FoundAsync(form));
        await bed.ApplyRunAsync("Animal", ["dog", "cat"]);
        var metadata = await bed.GetAsync();
        Assert.Equal(["cat"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
        Assert.Empty(metadata.TagSuggestions);
    }

    [Fact]
    public async Task ConfirmTag_CopiesBothForms_AndAReanalysisThatDropsItFlagsItRemoved()
    {
        await bed.ApplyRunAsync("Dog", ["dog"]);
        var ai = Assert.Single((await bed.GetAsync()).GetEffectiveTags());

        await EditAsync(new MetadataEdit.ConfirmTag(TagName.From("dog")));

        var accepted = Assert.Single((await bed.GetAsync()).GetEffectiveTags());
        Assert.Equal((MetadataSource.User, UserValueOrigin.Accepted), (accepted.Source, accepted.Origin));
        Assert.Equal((ai.OtherForm, ai.IsOtherFormDerived), (accepted.OtherForm, accepted.IsOtherFormDerived));
        await bed.ApplyRunAsync("Dog", ["cat"]);
        Assert.Equal(TagSuggestionKind.Removed, (await bed.GetAsync()).GetTagSuggestion(TagName.From("dog"))?.Kind);
    }

    [Fact]
    public async Task AcceptTagSuggestion_ConfirmsAnAddedTagAndDropsARemovedOne()
    {
        await bed.ApplyRunAsync("Animal", ["dog"]);
        await EditAsync(new MetadataEdit.ConfirmTag(TagName.From("dog")));
        await bed.ApplyRunAsync("Animal", ["sand"]);

        await EditAsync(new MetadataEdit.AcceptTagSuggestion(TagName.From("sand")), new MetadataEdit.AcceptTagSuggestion(TagName.From("dog")));

        var metadata = await bed.GetAsync();
        Assert.Equal(["sand"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
        Assert.Equal(MetadataSource.User, metadata.GetEffectiveTags()[0].Source);
        Assert.Empty(metadata.Rejections);
        Assert.False(metadata.HasNewAiSuggestions);
        Assert.Empty(await FoundAsync("dog"));
        await bed.ApplyRunAsync("Animal", ["sand"]);
        Assert.False((await bed.GetAsync()).HasNewAiSuggestions);
    }

    [Fact]
    public async Task DismissTagSuggestion_ARemovedTag_DoesntComeBackWhileRunsKeepLeavingItOut()
    {
        await bed.ApplyRunAsync("Dog", ["dog"]);
        await EditAsync(new MetadataEdit.ConfirmTag(TagName.From("dog")));
        await bed.ApplyRunAsync("Dog", ["cat"]);

        await EditAsync(new MetadataEdit.DismissTagSuggestion(TagName.From("dog")), new MetadataEdit.DismissTagSuggestion(TagName.From("cat")));

        Assert.False((await bed.GetAsync()).HasNewAiSuggestions);
        await bed.ApplyRunAsync("Dog", ["cat"]);
        var metadata = await bed.GetAsync();
        Assert.False(metadata.HasNewAiSuggestions);
        Assert.Contains(metadata.GetEffectiveTags(), tag => tag.Tag.Value == "dog" && tag.Source == MetadataSource.User);
    }

    [Fact]
    public async Task Edits_ManyItems_AreWrittenInBatchesAndUnknownItemsAreSkipped()
    {
        var items = new List<MediaItem> { bed.Item };
        for (var number = 2; number <= 230; number++)
        {
            items.Add(await bed.AddItemAsync(number));
        }

        var ids = items.Select(item => item.Id).Append(Id<MediaItem>.New()).ToList();

        var result = await bed.Sender.Send(new EditMetadataCommand(ids, [new MetadataEdit.AddTag("holiday")]), Token);

        Assert.Equal(items.Select(item => item.Id), result.ChangedItemIds);
        Assert.Equal(230, (await bed.Open.Search.FindKeywordMatchesAsync(SearchStoreTests.Search("holiday") with { Limit = 500 }, Token)).TotalCount);
    }

    [Fact]
    public async Task Edits_OneFails_NothingOfTheItemIsWritten()
    {
        await bed.ApplyRunAsync("Dog", ["dog"]);

        await using (var connection = bed.Library.Temporary.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_title BEFORE INSERT ON user_field_value WHEN NEW.value LIKE '%boom%' BEGIN SELECT RAISE(ABORT, 'interrupted'); END;";
            await command.ExecuteNonQueryAsync(Token);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => EditAsync(
            new MetadataEdit.RejectTag(TagName.From("dog")),
            new MetadataEdit.SetField(DescriptiveField.Notes, FieldValue.FromText("fine")),
            new MetadataEdit.SetField(Title, FieldValue.FromText("boom"))));

        var metadata = await bed.GetAsync();
        Assert.Empty(metadata.UserValues);
        Assert.Empty(metadata.Rejections);
        Assert.Empty(await FoundAsync("fine"));
        Assert.Equal([bed.Item.Id], await FoundAsync("dog"));
    }

    [Fact]
    public async Task RejectTag_InABulkEdit_TombstonesOnlyTheItemsThatShowTheTag()
    {
        var other = await bed.AddItemAsync(2);
        await bed.ApplyRunAsync("Dog", ["dog"]);

        var result = await bed.Sender.Send(new EditMetadataCommand([bed.Item.Id, other.Id], [new MetadataEdit.RejectTag(TagName.From("dog"))]), Token);

        Assert.Equal([bed.Item.Id], result.ChangedItemIds);
        Assert.Single((await bed.GetAsync()).Rejections);
        Assert.Empty((await bed.Open.Metadata.GetAsync(other.Id, Token)).Rejections);
    }

    [Fact]
    public async Task Edits_ThatAreInvalid_WriteNothingAndReportTheError()
    {
        var other = await bed.AddItemAsync(2);

        var result = await bed.Sender.Send(
            new EditMetadataCommand(
                [bed.Item.Id, other.Id],
                [new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")), new MetadataEdit.AddTag("holiday"), new MetadataEdit.AddTag("a, b")]),
            Token);

        Assert.Equal(MetadataValidationCode.TagInvalidCharacter, result.Error?.Code);
        Assert.Empty(result.ChangedItemIds);
        Assert.Empty((await bed.GetAsync()).UserValues);
        Assert.Empty(await FoundAsync("Rex"));
        Assert.Empty(await FoundAsync("holiday"));
        Assert.Empty(bed.Published);
    }

    [Fact]
    public async Task AddTag_TypedWithPunctuationAndWhiteSpace_IsStoredCleanedInTheCaseTyped()
    {
        await EditAsync(new MetadataEdit.AddTag("  #Summer_Trip. "));

        Assert.Contains((await bed.GetAsync()).GetEffectiveTags(), tag => tag.Tag.Value == "Summer Trip" && tag.Source == MetadataSource.User);
    }

    [Fact]
    public async Task AddTag_OverTheUserTagLimit_RefusesTheWholeBulkEditBeforeWritingAnyBatch()
    {
        var items = new List<MediaItem> { bed.Item };
        for (var number = 2; number <= 150; number++)
        {
            items.Add(await bed.AddItemAsync(number));
        }

        // The 120th item, in the second batch, already has the most tags a user may give an item.
        await bed.Open.Metadata.UpdateAsync(
            items[119].Id,
            metadata =>
            {
                for (var number = 0; number < MetadataLimits.UserTagsPerItem; number++)
                {
                    metadata.AddTag(TagName.From($"tag {number}"), bed.Library.Clock.Tick());
                }
            },
            Token);

        var result = await bed.Sender.Send(new EditMetadataCommand([.. items.Select(item => item.Id)], [new MetadataEdit.AddTag("trip")]), Token);

        Assert.Equal(MetadataValidationCode.TooManyTags, result.Error?.Code);
        Assert.Equal(items[119].Id, result.Error?.ItemId);
        Assert.Empty(result.ChangedItemIds);
        Assert.Empty(await FoundAsync("trip"));
        Assert.Empty(bed.Published);
    }

    [Fact]
    public async Task Edits_PublishOneChange_WithTheChangedItemsFieldsAndTags()
    {
        var other = await bed.AddItemAsync(2);

        await bed.Sender.Send(
            new EditMetadataCommand(
                [bed.Item.Id, other.Id],
                [new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")), new MetadataEdit.RevertField(DescriptiveField.Notes), new MetadataEdit.AddTag("dog")]),
            Token);

        var change = Assert.Single(bed.Published);
        Assert.Equal([bed.Item.Id, other.Id], change.ItemIds);
        Assert.Equal(ItemMetadataChange.Fields | ItemMetadataChange.Tags, change.Change);
        // Reverting the notes changed nothing (no user value), so only the title is named.
        Assert.Equal([DescriptiveField.Title], change.Fields.Order());
    }

    [Fact]
    public async Task Edits_PublishOnlyTheFieldsAndTagsThatReallyChanged()
    {
        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")));
        bed.Published.Clear();

        await EditAsync(new MetadataEdit.SetField(Title, FieldValue.FromText("Rex")), new MetadataEdit.AddTag("dog"));

        var change = Assert.Single(bed.Published);
        Assert.Equal(ItemMetadataChange.Tags, change.Change);
        Assert.Empty(change.Fields);
    }

    [Fact]
    public async Task Edits_ThatChangeNothing_PublishNothing()
    {
        var result = await EditAsync(new MetadataEdit.RevertField(Title), new MetadataEdit.DismissTagSuggestion(TagName.From("dog")));

        Assert.Empty(result.ChangedItemIds);
        Assert.Empty(bed.Published);
    }

    [Fact]
    public async Task Edits_ABulkEditThatFailsInALaterBatch_StillPublishesTheWrittenBatches()
    {
        var items = new List<MediaItem> { bed.Item };
        for (var number = 2; number <= 150; number++)
        {
            items.Add(await bed.AddItemAsync(number));
        }

        // The 120th item, in the second batch of 100, has a note that a trigger refuses to store again.
        await bed.Open.Metadata.UpdateAsync(items[119].Id, metadata => metadata.EditValue(DescriptiveField.Notes, FieldValue.FromText("boom"), bed.Library.Clock.Tick()), Token);
        await using (var connection = bed.Library.Temporary.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_note BEFORE INSERT ON user_field_value WHEN NEW.value LIKE '%boom%' BEGIN SELECT RAISE(ABORT, 'interrupted'); END;";
            await command.ExecuteNonQueryAsync(Token);
        }

        await Assert.ThrowsAnyAsync<Exception>(
            () => bed.Sender.Send(new EditMetadataCommand([.. items.Select(item => item.Id)], [new MetadataEdit.AddTag("trip")]), Token));

        var change = Assert.Single(bed.Published);
        Assert.Equal(items.Take(100).Select(item => item.Id), change.ItemIds);
        Assert.Equal(100, (await bed.Open.Search.FindKeywordMatchesAsync(SearchStoreTests.Search("trip") with { Limit = 500 }, Token)).TotalCount);
    }

    [Fact]
    public async Task Edits_AFailingWriteIsReported_EvenWhenAViewFailsOnTheChangeThatWasWritten()
    {
        var items = new List<MediaItem> { bed.Item };
        for (var number = 2; number <= 150; number++)
        {
            items.Add(await bed.AddItemAsync(number));
        }

        await bed.Open.Metadata.UpdateAsync(items[119].Id, metadata => metadata.EditValue(DescriptiveField.Notes, FieldValue.FromText("boom"), bed.Library.Clock.Tick()), Token);
        await using (var connection = bed.Library.Temporary.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_note BEFORE INSERT ON user_field_value WHEN NEW.value LIKE '%boom%' BEGIN SELECT RAISE(ABORT, 'interrupted'); END;";
            await command.ExecuteNonQueryAsync(Token);
        }

        bed.FailPublishing = true;

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => bed.Sender.Send(new EditMetadataCommand([.. items.Select(item => item.Id)], [new MetadataEdit.AddTag("trip")]), Token));

        Assert.DoesNotContain("handler failed", failure.ToString());
    }

    private Task<EditMetadataResult> EditAsync(params MetadataEdit[] edits) =>
        bed.Sender.Send(new EditMetadataCommand([bed.Item.Id], edits), Token);

    private async Task<IReadOnlyList<Id<MediaItem>>> FoundAsync(string word) =>
        [.. (await bed.Open.Search.FindKeywordMatchesAsync(SearchStoreTests.Search(word), Token)).Matches.Select(match => match.ItemId)];
}
