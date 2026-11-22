using System.Diagnostics;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Metadata.GetItemMetadata;
using MediaFileClassifier.UseCases.Metadata.GetItemsMetadata;

namespace MediaFileClassifier.Infrastructure.Tests.Metadata;

/// <summary>
/// Tests for the metadata read model (E07-S01-T01): <see cref="GetItemMetadataQuery"/> and <see cref="GetItemsMetadataQuery"/>
/// against a real library.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class ItemMetadataReadTests : IAsyncLifetime
{
    private MetadataTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await MetadataTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Get_ItemWithoutMetadata_HasEveryFieldEmpty()
    {
        var view = await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token);

        Assert.NotNull(view);
        Assert.Equal(DescriptiveFields.All, view.Fields.Select(field => field.Field));
        Assert.All(view.Fields, field => Assert.Null(field.Effective));
        Assert.Empty(view.Tags);
        Assert.False(view.HasNewAiSuggestions);
    }

    [Fact]
    public async Task Get_UserValueOverAiValue_ShowsEveryLayerWithProvenance()
    {
        await bed.Open.Metadata.UpdateAsync(
            bed.Item.Id,
            metadata => metadata.SetTechnicalValues([new TechnicalFieldValue(bed.Item.Id, DescriptiveField.Title, FieldValue.FromText("IMG_1"), bed.Library.Clock.Tick())]),
            Token);
        var run = await bed.ApplyRunAsync("Dog on a beach", ["dog"]);
        await bed.Open.Metadata.UpdateAsync(bed.Item.Id, metadata => metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Rex"), bed.Library.Clock.Tick()), Token);

        var title = (await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token))![DescriptiveField.Title];

        Assert.Equal("Rex", title.Effective?.Value.Text);
        Assert.Equal(MetadataSource.User, title.Effective?.Source);
        Assert.Equal(UserValueOrigin.Edited, title.User?.Origin);
        Assert.Equal("Dog on a beach", title.LatestAi?.Value.Text);
        Assert.Equal(run.Id, title.LatestAi?.RunId);
        Assert.Equal(0.9, title.LatestAi?.Confidence);
        Assert.Equal("IMG_1", title.Technical?.Value.Text);
        Assert.False(title.HasNewAiSuggestion);
    }

    [Fact]
    public async Task Get_AiValue_NamesTheModelThatWroteIt()
    {
        var run = await bed.ApplyRunAsync("Dog on a beach", ["dog"]);

        var view = (await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token))!;

        var modelId = view[DescriptiveField.Title].Effective!.ModelId!.Value;
        Assert.Equal(run.ModelId, modelId);
        Assert.False(string.IsNullOrWhiteSpace(view.ModelNames[modelId]));
    }

    [Fact]
    public async Task Get_ReanalysisWithADifferentValue_ExposesTheSuggestionAndTheFlag()
    {
        await bed.ApplyRunAsync("Dog on a beach", ["dog"]);
        await bed.Open.Metadata.UpdateAsync(bed.Item.Id, metadata => metadata.AcceptAiValue(DescriptiveField.Title, bed.Library.Clock.Tick()), Token);
        var second = await bed.ApplyRunAsync("Dog running", ["dog", "sand"]);

        var view = (await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token))!;

        var title = view[DescriptiveField.Title];
        Assert.Equal("Dog on a beach", title.Effective?.Value.Text);
        Assert.Equal(UserValueOrigin.Accepted, title.User?.Origin);
        Assert.Equal("Dog running", title.Suggestion?.Value.Text);
        Assert.Equal(second.Id, title.Suggestion?.RunId);
        Assert.True(view.HasNewAiSuggestions);
        Assert.Equal(TagSuggestionKind.Added, Assert.Single(view.Tags, tag => tag.Tag.Tag.Value == "sand").Suggestion);
        Assert.Null(Assert.Single(view.Tags, tag => tag.Tag.Tag.Value == "dog").Suggestion);
    }

    [Fact]
    public async Task Get_RejectedTag_IsListedAsRejectedAndNotEffective()
    {
        await bed.ApplyRunAsync("Dog", ["dog", "cat"]);
        await bed.Open.Metadata.UpdateAsync(bed.Item.Id, metadata => metadata.RejectTag(TagName.From("cat"), bed.Library.Clock.Tick()), Token);

        var view = (await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token))!;

        Assert.Equal(["dog"], view.Tags.Select(tag => tag.Tag.Tag.Value));
        Assert.Equal("cat", Assert.Single(view.RejectedTags).Tag.Value);
    }

    [Fact]
    public async Task GetMany_SeveralItems_ReturnsEachOnceInTheOrderGiven()
    {
        var other = await bed.AddItemAsync(2);
        await bed.ApplyRunAsync("First", ["a"]);
        await bed.ApplyRunAsync("Second", ["b"], other);

        var views = await bed.Sender.Send(new GetItemsMetadataQuery([other.Id, bed.Item.Id, other.Id]), Token);

        Assert.Equal([other.Id, bed.Item.Id], views.Select(view => view.ItemId));
        Assert.Equal(["Second", "First"], views.Select(view => view[DescriptiveField.Title].Effective?.Value.Text));
    }

    [Fact]
    public async Task Get_RichItem_LoadsInUnder20Milliseconds()
    {
        await MakeRichAsync();
        for (var i = 0; i < 5; i++)
        {
            await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token);
        }

        var times = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var watch = Stopwatch.StartNew();
            var view = await bed.Sender.Send(new GetItemMetadataQuery(bed.Item.Id), Token);
            times.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(60, view!.Tags.Count);
        }

        times.Sort();
        var median = times[times.Count / 2];
        TestContext.Current.SendDiagnosticMessage($"median {median:F2} ms, p90 {times[45]:F2} ms, max {times[^1]:F2} ms");
        if (TestPerformance.Enabled)
        {
            Assert.True(median < 20, $"Loading one item's metadata took {median:F1} ms (median of 50).");
        }
    }

    // Two analyzers with an active and a retained run each, every field set by AI, user values on a few, technical
    // values, 40 tags per run, 10 accepted and 30 rejected tags: more than a typical item has.
    private async Task MakeRichAsync()
    {
        var clock = bed.Library.Clock;
        var id = bed.Item.Id;
        await bed.Open.Metadata.UpdateAsync(
            id,
            metadata =>
            {
                metadata.SetTechnicalValues([
                    new TechnicalFieldValue(id, DescriptiveField.Title, FieldValue.FromText("IMG_00001"), clock.Tick()),
                    new TechnicalFieldValue(id, DescriptiveField.Description, FieldValue.FromText("Camera caption"), clock.Tick()),
                ]);
                metadata.SetTechnicalTags(Enumerable.Range(0, 10).Select(i => TagName.From($"keyword {i}")), clock.Tick());
            },
            Token);
        foreach (var analyzer in new[] { "describe", "ocr" })
        {
            for (var round = 0; round < 2; round++)
            {
                var run = AnalysisRun.Start(id, analyzer, bed.Library.Model, "p1", clock.Tick());
                run.Complete(clock.Tick());
                var values = DescriptiveFields.All
                    .Where(field => !DescriptiveFields.IsUserOnly(field))
                    .Select(field => AiFieldValue.For(
                        run,
                        field,
                        DescriptiveFields.IsList(field)
                            ? FieldValue.FromList(Enumerable.Range(0, 8).Select(i => $"{field} {i} {round}"))
                            : FieldValue.FromText(string.Concat(Enumerable.Repeat($"{field} text {round} ", 40))),
                        0.7,
                        clock.GetUtcNow()))
                    .ToList();
                var tags = Enumerable.Range(0, 40).Select(i => TagAssignment.Ai(run, TagName.From($"{analyzer} tag {i}"), 0.6, clock.GetUtcNow())).ToList();
                await bed.Open.Metadata.UpdateAsync(id, metadata => metadata.ApplyRun(run, values, tags, 1), Token);
            }
        }

        await bed.Open.Metadata.UpdateAsync(
            id,
            metadata =>
            {
                metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("My title"), clock.Tick());
                metadata.AcceptAiValue(DescriptiveField.Summary, clock.Tick());
                metadata.EditValue(DescriptiveField.Notes, FieldValue.FromText("A note"), clock.Tick());
                for (var i = 0; i < 10; i++)
                {
                    metadata.AcceptTag(TagName.From($"describe tag {i}"), clock.Tick());
                }

                for (var i = 0; i < 20; i++)
                {
                    metadata.RejectTag(TagName.From($"ocr tag {i}"), clock.Tick());
                }

                for (var i = 0; i < 10; i++)
                {
                    metadata.RejectTag(TagName.From($"keyword {i}"), clock.Tick());
                }
            },
            Token);
    }
}
