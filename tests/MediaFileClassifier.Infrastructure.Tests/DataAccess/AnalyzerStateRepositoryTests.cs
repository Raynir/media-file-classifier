using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="AnalyzerStateRepository"/> (E06-S01-T03) against a real library database.
/// </summary>
public sealed class AnalyzerStateRepositoryTests : IAsyncLifetime
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
    public async Task SetAsync_ReplacesTheAnalyzersEarlierState()
    {
        await bed.Library.AnalyzerStates.SetAsync(
            [
                AnalyzerState.Retrying(item.Id, AnalyzerKeys.Ocr, AnalyzerReasons.ProviderFailed, "down", "Unreachable", bed.Clock.Tick()),
                AnalyzerState.Done(item.Id, AnalyzerKeys.Embedding, null, bed.Clock.Tick()),
            ],
            Token);
        var failed = AnalyzerState.Failed(item.Id, AnalyzerKeys.Ocr, AnalyzerReasons.ProviderFailed, "down", "Unreachable", true, bed.Clock.Tick());

        await bed.Library.AnalyzerStates.SetAsync([failed], Token);

        var states = await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token);
        Assert.Equal(2, states.Count);
        Assert.Equal(failed, states.Single(state => state.Analyzer == AnalyzerKeys.Ocr));
        Assert.Equal(AnalyzerStatus.Done, states.Single(state => state.Analyzer == AnalyzerKeys.Embedding).Status);
    }

    [Fact]
    public async Task GetItemsAsync_FindsTheItemsByAnalyzerAndStatus()
    {
        var other = bed.NewItem(2);
        await bed.Library.Items.AddAsync(other, Token);
        var now = bed.Clock.Tick();
        await bed.Library.AnalyzerStates.SetAsync(
            [
                AnalyzerState.Failed(item.Id, AnalyzerKeys.Ocr, AnalyzerReasons.ProviderFailed, null, null, true, now),
                AnalyzerState.Done(other.Id, AnalyzerKeys.Ocr, null, now),
                AnalyzerState.Failed(other.Id, AnalyzerKeys.Describer, AnalyzerReasons.ProviderFailed, null, null, true, now),
            ],
            Token);

        Assert.Equal([item.Id], await bed.Library.AnalyzerStates.GetItemsAsync(AnalyzerKeys.Ocr, AnalyzerStatus.Failed, Token));
        Assert.Equal([other.Id], await bed.Library.AnalyzerStates.GetItemsAsync(AnalyzerKeys.Describer, AnalyzerStatus.Failed, Token));
    }

    [Fact]
    public async Task ApplyRunAsync_StoresTheRunItsValuesAndTheState()
    {
        var run = AnalysisRun.Start(item.Id, AnalyzerKeys.Describer, bed.Model, "p1", bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        var now = bed.Clock.GetUtcNow();
        var state = AnalyzerState.Done(item.Id, AnalyzerKeys.Describer, run.Id, now);

        await bed.Library.AnalyzerStates.ApplyRunAsync(
            run,
            [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText("Dog"), null, now)],
            [TagAssignment.Ai(run, TagName.From("dog"), null, now)],
            1,
            state,
            Token);

        var metadata = await bed.Library.Metadata.GetAsync(item.Id, Token);
        Assert.Equal("Dog", metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text);
        Assert.Equal(run.Id, Assert.Single(metadata.Runs).Id);
        Assert.Equal("dog", Assert.Single(metadata.GetEffectiveTags()).Tag.Value);
        Assert.Equal(state, Assert.Single(await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token)));
    }

    [Fact]
    public async Task ApplyRunAsync_FailingRun_StoresNeitherTheRunNorTheState()
    {
        var run = AnalysisRun.Start(item.Id, AnalyzerKeys.Ocr, bed.Model, null, bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        var foreign = AnalysisRun.Start(item.Id, AnalyzerKeys.Ocr, bed.Model, null, bed.Clock.Tick());
        var now = bed.Clock.GetUtcNow();

        // A value from another run makes ApplyRun throw inside the write.
        await Assert.ThrowsAsync<ArgumentException>(() => bed.Library.AnalyzerStates.ApplyRunAsync(
            run,
            [AiFieldValue.For(foreign, DescriptiveField.OcrText, FieldValue.FromText("text"), null, now)],
            [],
            1,
            AnalyzerState.Done(item.Id, AnalyzerKeys.Ocr, run.Id, now),
            Token));

        Assert.Empty((await bed.Library.Metadata.GetAsync(item.Id, Token)).Runs);
        Assert.Empty(await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token));
    }

    [Fact]
    public async Task GetOutdatedAsync_FindsActiveRunsFromAnotherModelOrPrompt()
    {
        var other = ModelRegistration.Create("local", "clip-2", "1", null, null, LibraryTestBed.Start);
        await bed.Library.Models.AddAsync(other, Token);
        var current = await AddItemAsync(2);
        var oldPrompt = await AddItemAsync(3);
        var missing = await AddItemAsync(4, missing: true);
        await ApplyAsync(item, AnalyzerKeys.Describer, other, "p1");
        await ApplyAsync(current, AnalyzerKeys.Describer, bed.Model, "p1");
        await ApplyAsync(oldPrompt, AnalyzerKeys.Describer, bed.Model, "p0");
        await ApplyAsync(missing, AnalyzerKeys.Describer, other, "p1");

        // The current model's run of the first item replaced the old one, which stays as the previous run.
        await ApplyAsync(current, AnalyzerKeys.Ocr, other, null);
        await ApplyAsync(current, AnalyzerKeys.Ocr, bed.Model, null);

        var outdated = await bed.Library.AnalyzerStates.GetOutdatedAsync(
            [
                new AnalyzerProvenance(AnalyzerKeys.Describer, "local", "clip", "1", "p1"),
                new AnalyzerProvenance(AnalyzerKeys.Ocr, "local", "clip", null, null),
            ],
            Token);

        Assert.Equivalent(new[] { item.Id, oldPrompt.Id }, outdated.For(AnalyzerKeys.Describer), strict: true);
        Assert.Empty(outdated.For(AnalyzerKeys.Ocr));
        Assert.Equal(2, outdated.Count);
    }

    [Fact]
    public async Task GetOutdatedAsync_ComparesTheModelVersionOnlyWhenBothHaveOne()
    {
        var unversioned = ModelRegistration.Create("local", "clip", null, null, null, LibraryTestBed.Start);
        await bed.Library.Models.AddAsync(unversioned, Token);
        var second = await AddItemAsync(2);
        await ApplyAsync(item, AnalyzerKeys.Ocr, bed.Model, null);
        await ApplyAsync(second, AnalyzerKeys.Ocr, unversioned, null);

        var newVersion = await bed.Library.AnalyzerStates.GetOutdatedAsync([new AnalyzerProvenance(AnalyzerKeys.Ocr, "local", "clip", "2", null)], Token);
        var noVersion = await bed.Library.AnalyzerStates.GetOutdatedAsync([new AnalyzerProvenance(AnalyzerKeys.Ocr, "local", "clip", null, null)], Token);

        Assert.Equal([item.Id], newVersion.ItemIds);
        Assert.Empty(noVersion.ItemIds);
    }

    [Fact]
    public async Task GetOutdatedAsync_Embedding_FindsItemsWithoutAVectorOfTheCurrentModel()
    {
        var other = ModelRegistration.Create("local", "siglip-2", "1", null, 4, LibraryTestBed.Start);
        await bed.Library.Models.AddAsync(other, Token);
        var both = await AddItemAsync(2);
        var current = await AddItemAsync(3);
        await AddItemAsync(4);
        await EmbedAsync(item, bed.Model);
        await EmbedAsync(both, bed.Model);
        await EmbedAsync(both, other);
        await EmbedAsync(current, other);

        var outdated = await bed.Library.AnalyzerStates.GetOutdatedAsync([new AnalyzerProvenance(AnalyzerKeys.Embedding, "local", "siglip-2", "1", null)], Token);

        Assert.Equal([item.Id], outdated.For(AnalyzerKeys.Embedding));
    }

    [Fact]
    public async Task GetOutdatedAsync_AnalyzerLeftOut_IsNeverOutdated()
    {
        var other = ModelRegistration.Create("local", "clip-2", "1", null, null, LibraryTestBed.Start);
        await bed.Library.Models.AddAsync(other, Token);
        await ApplyAsync(item, AnalyzerKeys.Describer, other, "p1");

        var outdated = await bed.Library.AnalyzerStates.GetOutdatedAsync([new AnalyzerProvenance(AnalyzerKeys.Ocr, "local", "clip", "1", null)], Token);

        Assert.Equal(0, outdated.Count);
    }

    [Fact]
    public async Task SetAsync_UnknownItem_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => bed.Library.AnalyzerStates.SetAsync(
            [AnalyzerState.Done(Id<MediaItem>.New(), AnalyzerKeys.Ocr, null, bed.Clock.Tick())],
            Token));
    }

    [Fact]
    public async Task DeletingTheItem_DeletesItsStates()
    {
        await bed.Library.AnalyzerStates.SetAsync([AnalyzerState.Done(item.Id, AnalyzerKeys.Embedding, null, bed.Clock.Tick())], Token);

        Assert.True(await bed.Library.Items.DeleteAsync(item.Id, Token));

        Assert.Empty(await bed.Library.AnalyzerStates.GetItemsAsync(AnalyzerKeys.Embedding, AnalyzerStatus.Done, Token));
    }

    [Fact]
    public async Task State_SurvivesReopening()
    {
        var state = AnalyzerState.Skipped(item.Id, AnalyzerKeys.Describer, AnalyzerReasons.ModelMissing, true, bed.Clock.Tick(), "no model");
        await bed.Library.AnalyzerStates.SetAsync([state], Token);

        await bed.ReopenAsync();

        Assert.Equal(state, Assert.Single(await bed.Library.AnalyzerStates.GetForItemAsync(item.Id, Token)));
    }

    private async Task<MediaItem> AddItemAsync(int number, bool missing = false)
    {
        var added = bed.NewItem(number);
        if (missing)
        {
            added.ChangeStatus(MediaItemStatus.Missing, bed.Clock.Tick());
        }

        await bed.Library.Items.AddAsync(added, Token);
        return added;
    }

    private async Task ApplyAsync(MediaItem target, string analyzer, ModelRegistration model, string? promptVersion)
    {
        var run = AnalysisRun.Start(target.Id, analyzer, model, promptVersion, bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        await bed.Library.AnalyzerStates.ApplyRunAsync(run, [], [], 1, AnalyzerState.Done(target.Id, analyzer, run.Id, bed.Clock.Tick()), Token);
    }

    private Task EmbedAsync(MediaItem target, ModelRegistration model) =>
        bed.Library.Embeddings.ReplaceAsync(target.Id, model.Id, [Embedding.Create(target.Id, model, EmbeddingKind.Image, [1, 0, 0, 0], bed.Clock.Tick())], Token);
}
