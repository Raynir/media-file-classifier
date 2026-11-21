using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.Abstractions.Thumbnails;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.Media;
using MediaFileClassifier.UseCases.Exclusions;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;
using MediaFileClassifier.UseCases.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.Exclusions;

/// <summary>
/// Removing items from the library with a real library, import and job system (E02-S10-T01, D024).
/// </summary>
public sealed class ExcludeItemsFlowTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await ImportTestBed.CreateAsync();
        bed.Copy(SampleMedia.JpegWithExif, "a/photo.jpg");
        bed.Copy(SampleMedia.PngWithAlpha, "b/alpha.png");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
    }

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Remove_KeepsIdentityUserValuesAndFavorite_AndDropsAiResultsEmbeddingsAndThumbnails()
    {
        var item = await ItemAsync("a/photo.jpg");
        await AddAiResultsAsync(item.Id, "Lighthouse");
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata =>
        {
            metadata.EditValue(DescriptiveField.Description, FieldValue.FromText("Seaside walk"), LibraryTestBed.Start);
            metadata.AddTag(TagName.From("holiday"), LibraryTestBed.Start);
        }, Token);
        await bed.Library.Items.SetFavoriteAsync([item.Id], true, LibraryTestBed.Start, Token);
        var document = Assert.Single((await bed.Library.Search.FindKeywordMatchesAsync(SearchStoreTests.Search("lighthouse"), Token)).Matches).DocumentId;
        var thumbnail = bed.Library.Layout.GetThumbnailPath(item.Id, ThumbnailSize.Small);
        Assert.True(File.Exists(thumbnail));

        var result = await bed.Sender.Send(new ExcludeItemsCommand([item.Id]), Token);

        Assert.Equal(new ExcludeItemsResult(1, CleanupQueued: false), result);
        var stored = (await bed.Library.Items.GetAsync(item.Id, Token))!;
        Assert.True(LibraryExclusions.IsRemovedByUser(stored));
        Assert.Equal(item.Path, stored.Path);
        Assert.Equal(item.Hashes, stored.Hashes);
        Assert.True(stored.IsFavorite);
        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Empty(metadata.Runs);
        Assert.Empty(metadata.AiValues);
        Assert.Equal("Seaside walk", metadata.GetEffectiveValue(DescriptiveField.Description)?.Value.Text);
        Assert.Null(metadata.GetEffectiveValue(DescriptiveField.Title));
        Assert.Equal(["holiday"], metadata.GetEffectiveTags().Select(tag => tag.Tag.Value));
        Assert.Empty(await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token));
        Assert.Empty(await bed.Library.Embeddings.GetAsync(item.Id, Token));
        Assert.False(File.Exists(thumbnail));

        // The search text lost the AI title and kept the user's description.
        var fields = await bed.Library.Search.GetMatchedFieldsAsync([document], ["lighthouse", "seaside"], Token);
        Assert.Equal(SearchFields.Description, fields[document]);
    }

    [Fact]
    public async Task Remove_TakesTheItemOutOfBrowsingAndSearch_AndLeavesTheOthers()
    {
        var removed = await ItemAsync("a/photo.jpg");
        var kept = await ItemAsync("b/alpha.png");

        await bed.Sender.Send(new ExcludeItemsCommand([removed.Id]), Token);

        var browsed = await bed.Library.Items.GetSummariesAsync(new MediaItemFilter(), 0, 10, Token);
        Assert.Equal([kept.Id], browsed.Select(summary => summary.Id));
        Assert.Equal(1, await bed.Library.Items.CountAsync(new MediaItemFilter(), Token));
        var documents = await bed.Library.Search.GetDocumentIdsAsync(SearchFilter.None, Token);
        Assert.Single(documents);
    }

    [Fact]
    public async Task Remove_AlreadyExcludedByTheRules_KeepsTheRulesReason()
    {
        var item = await ItemAsync("a/photo.jpg");
        item.ChangeStatus(MediaItemStatus.Excluded, LibraryTestBed.Start, IndexedFolders.ExcludedByRules);
        await bed.Library.Items.UpdateAsync(item, Token);

        var result = await bed.Sender.Send(new ExcludeItemsCommand([item.Id]), Token);

        Assert.Equal(0, result.Removed);
        Assert.Equal(IndexedFolders.ExcludedByRules, (await bed.Library.Items.GetAsync(item.Id, Token))!.StatusReason);
    }

    [Fact]
    public async Task CleanUp_ItemRestoredFirst_KeepsItsData()
    {
        var item = await ItemAsync("a/photo.jpg");
        await AddAiResultsAsync(item.Id, "Lighthouse");

        // Not removed by the user (e.g. restored before the job ran): the cleanup leaves it alone.
        var cleaned = await ExclusionCleanupStage.CleanUpAsync(bed.Library, bed.Get<IThumbnailCache>(), [item.Id], Token);

        Assert.Equal(0, cleaned);
        Assert.Single((await bed.Library.Metadata.GetAsync(item.Id, Token)).Runs);
        Assert.Single(await bed.Library.Embeddings.GetAsync(item.Id, Token));
    }

    [Fact]
    public void Payload_RoundTripsTheBatch()
    {
        Id<MediaItem>[] ids = [Id<MediaItem>.New(), Id<MediaItem>.New()];

        Assert.Equal(ids, ExclusionCleanupStage.FromPayload(ExclusionCleanupStage.ToPayload(ids)));
        Assert.Empty(ExclusionCleanupStage.FromPayload(null));
    }

    private async Task<MediaItem> ItemAsync(string path) =>
        (await bed.ItemsAsync()).Single(item => item.Path.Value == path);

    private async Task AddAiResultsAsync(Id<MediaItem> itemId, string title)
    {
        var model = bed.LibraryBed.Model;
        var now = LibraryTestBed.Start;
        var run = AnalysisRun.Start(itemId, AnalyzerKeys.Describer, model, "p1", now);
        run.Complete(now);
        await bed.Library.AnalyzerStates.ApplyRunAsync(
            run,
            [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title), null, now)],
            [TagAssignment.Ai(run, TagName.From("lamp"), null, now)],
            1,
            AnalyzerState.Done(itemId, AnalyzerKeys.Describer, run.Id, now),
            Token);
        await bed.Library.Embeddings.ReplaceAsync(itemId, model.Id, [Embedding.Create(itemId, model, EmbeddingKind.Image, [0.5f, 0.5f, 0.5f, 0.5f], now)], Token);
    }
}
