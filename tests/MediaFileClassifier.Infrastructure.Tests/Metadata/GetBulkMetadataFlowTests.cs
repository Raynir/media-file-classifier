using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Tests.TestSupport;
using MediaFileClassifier.Testing.Timing;
using MediaFileClassifier.UseCases.Metadata.EditMetadata;
using MediaFileClassifier.UseCases.Metadata.GetBulkMetadata;

namespace MediaFileClassifier.Infrastructure.Tests.Metadata;

/// <summary>
/// Tests for the aggregated metadata of a selection (E07-S03-T01) through <see cref="GetBulkMetadataQuery"/> against a
/// real library.
/// </summary>
[Collection(TimingTestsCollection.Name)]
public sealed class GetBulkMetadataFlowTests : IAsyncLifetime
{
    private MetadataTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await MetadataTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task Aggregates_SharedAndDifferingValues_AndTagCountsBySource()
    {
        var second = await bed.AddItemAsync(2);
        var third = await bed.AddItemAsync(3);
        await bed.ApplyRunAsync("Dog on a beach", ["dog", "sea"]);
        await bed.ApplyRunAsync("Dog on a beach", ["dog"], second);
        await bed.ApplyRunAsync("Cat", ["cat"], third);
        await Edit([bed.Item.Id, second.Id], new MetadataEdit.AddTag("holiday"));
        await Edit([bed.Item.Id, second.Id, third.Id], new MetadataEdit.SetField(DescriptiveField.Notes, FieldValue.FromText("Italy")));

        var view = await bed.Sender.Send(new GetBulkMetadataQuery([bed.Item.Id, second.Id, third.Id, bed.Item.Id]), Token);

        Assert.Equal(3, view.ItemCount);
        var title = view[DescriptiveField.Title];
        Assert.True(title.IsMixed);
        Assert.Null(title.Shared);
        Assert.Equal((3, 3), (title.ItemsWithValue, title.AiValueCount));
        var notes = view[DescriptiveField.Notes];
        Assert.False(notes.IsMixed);
        Assert.Equal("Italy", notes.Shared?.Text);
        Assert.Equal(3, notes.UserValueCount);
        var summary = view[DescriptiveField.Summary];
        Assert.False(summary.IsMixed);
        Assert.Null(summary.Shared);
        var counts = view.Tags.ToDictionary(tag => tag.Tag.Value, tag => (tag.ItemCount, tag.UserCount, tag.AiCount));
        Assert.Equal((2, 2, 0), counts["holiday"]);
        Assert.Equal((2, 0, 2), counts["dog"]);
        Assert.Equal((1, 0, 1), counts["cat"]);
        Assert.Equal(["dog", "holiday"], view.Tags.Take(2).Select(tag => tag.Tag.Value).Order());
    }

    [Fact]
    public async Task AFieldSetOnSomeItemsOnly_IsMixed()
    {
        var second = await bed.AddItemAsync(2);
        await Edit([bed.Item.Id], new MetadataEdit.SetField(DescriptiveField.Notes, FieldValue.FromText("Italy")));

        var view = await bed.Sender.Send(new GetBulkMetadataQuery([bed.Item.Id, second.Id]), Token);

        var notes = view[DescriptiveField.Notes];
        Assert.True(notes.IsMixed);
        Assert.Null(notes.Shared);
        Assert.Equal(1, notes.ItemsWithValue);
    }

    [Fact]
    public async Task NoItems_GiveAnEmptyView()
    {
        var view = await bed.Sender.Send(new GetBulkMetadataQuery([]), Token);

        Assert.Equal(BulkMetadataView.Empty, view);
    }

    [Fact]
    public async Task FiveHundredItems_AreAggregatedWithinASecond()
    {
        var ids = new List<Id<MediaItem>> { bed.Item.Id };
        for (var number = 2; number <= 500; number++)
        {
            ids.Add((await bed.AddItemAsync(number)).Id);
        }

        await Edit(ids, new MetadataEdit.AddTag("holiday"), new MetadataEdit.SetField(DescriptiveField.Title, FieldValue.FromText("Trip")));
        var clock = Stopwatch.StartNew();

        var view = await bed.Sender.Send(new GetBulkMetadataQuery(ids), Token);

        clock.Stop();
        TestContext.Current.SendDiagnosticMessage($"Aggregated 500 items in {clock.ElapsedMilliseconds} ms");
        Assert.Equal(500, view.ItemCount);
        Assert.Equal(500, view.Tags.Single().ItemCount);
        if (TestPerformance.Enabled)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Aggregating 500 items took {clock.ElapsedMilliseconds} ms.");
        }
    }

    private Task Edit(IReadOnlyCollection<Id<MediaItem>> ids, params MetadataEdit[] edits) =>
        bed.Sender.Send(new EditMetadataCommand(ids, edits), Token);
}
