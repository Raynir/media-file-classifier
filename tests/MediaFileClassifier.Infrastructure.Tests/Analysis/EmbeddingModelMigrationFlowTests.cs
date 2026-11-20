using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Search;
using MediaFileClassifier.Infrastructure.Tests.Import;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Analysis.Outdated;
using MediaFileClassifier.UseCases.Analysis.UpdateAnalysisProfile;
using MediaFileClassifier.UseCases.Embeddings;
using MediaFileClassifier.UseCases.Search.FindSimilar;
using MediaFileClassifier.UseCases.Search.SearchMedia;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Infrastructure.Tests.Analysis;

/// <summary>
/// A simulated embedding model migration (E06-S07-T05), on imported photos analyzed by the real pipeline and searched
/// through the real search index: search keeps working before, during and after the switch to the new model.
/// </summary>
public sealed class EmbeddingModelMigrationFlowTests : IAsyncDisposable
{
    private const string Query = "lighthouse";

    private readonly FakeEmbeddingProvider older = new();
    private readonly FakeEmbeddingProvider newer = new(modelName: "fake-embedder-2");
    private readonly FakeImageDescriber describer = new(describe: (_, _) =>
        new ImageDescription("Lighthouse", "A lighthouse on the coast", "Lighthouse", ["coast"], [], [], [], []));

    private ImportTestBed? bed;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (bed is not null)
        {
            await bed.DisposeAsync();
        }
    }

    [Fact]
    public async Task Search_ThroughoutAModelMigration_FindsEveryItem_AndSkipsOnlyTheOutdatedOnesAfterTheSwitch()
    {
        var items = await AnalyzedAsync(4);
        var migration = bed!.Get<EmbeddingModelMigration>();
        await migration.Pending;
        var oldModel = await ModelAsync(older);
        Assert.Equal(oldModel, ActiveModel);

        // Before the re-index: the old model's vectors find every item.
        var before = await SearchAsync();
        Assert.Equal(4, before.Hits.Count(hit => hit.Similarity is not null));
        Assert.Equal(0, before.SkippedCount);

        // A quarter re-indexed with the new model: search stays on the old one, which still has every item.
        await bed.Library.UpdateLibraryAsync(WithThreshold(0.5), Token);
        await bed.Sender.Send(new UpdateAnalysisProfileCommand(AnalysisProfile.Default.With(new AnalyzerSettings(AnalyzerKeys.Embedding) { ModelId = newer.Model.ModelName })), Token);
        await ReindexAsync(items[0]);
        Assert.False(await migration.CheckAsync(Token));
        var newModel = await ModelAsync(newer);
        Assert.Equal(oldModel, ActiveModel);
        var part = await SearchAsync();
        Assert.Equal(4, part.Hits.Count(hit => hit.Similarity is not null));
        Assert.Equal(0, part.SkippedCount);
        Assert.Equal(3, (await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token)).For(AnalyzerKeys.Embedding).Count);

        // Half re-indexed reaches the threshold: search switches, and the outdated half is skipped but found by keyword.
        await ReindexAsync(items[1]);
        Assert.True(await migration.CheckAsync(Token));
        Assert.Equal(newModel, ActiveModel);
        var calls = (Old: older.Calls, New: newer.Calls);
        var after = await SearchAsync();
        Assert.Equal((calls.Old, calls.New + 1), (older.Calls, newer.Calls));
        Assert.Equal(Set(items[0], items[1]), after.Hits.Where(hit => hit.Similarity is not null).Select(hit => hit.ItemId).ToHashSet());
        Assert.Equal(Set(items[2], items[3]), after.Hits.Where(hit => hit.Similarity is null && hit.KeywordRank is not null).Select(hit => hit.ItemId).ToHashSet());
        Assert.Equal(2, after.SkippedCount);
        Assert.Equal(Set(items[2], items[3]), (await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token)).For(AnalyzerKeys.Embedding).ToHashSet());
        Assert.Equal([newModel], await ModelsOfAsync(items[0]));
        Assert.Equal([oldModel], await ModelsOfAsync(items[2]));

        var similar = await bed.Sender.Send(new FindSimilarQuery(items[0].Id), Token);
        Assert.Equal(SemanticSearchStatus.Used, similar.Semantic);
        Assert.Equal([items[1].Id], similar.Hits.Select(hit => hit.ItemId));
        Assert.Equal(2, similar.SkippedCount);
        Assert.Equal(SemanticSearchStatus.Outdated, (await bed.Sender.Send(new FindSimilarQuery(items[2].Id), Token)).Semantic);

        // The rest re-indexed: every item is found with the new model, and the old vectors are gone.
        await bed.Sender.Send(new AnalyzeScopeCommand(AnalysisScope.Outdated), Token);
        await bed.WaitUntilIdleAsync();
        var done = await SearchAsync();
        Assert.Equal(4, done.Hits.Count(hit => hit.Similarity is not null));
        Assert.Equal(0, done.SkippedCount);
        foreach (var item in items)
        {
            Assert.Equal([newModel], await ModelsOfAsync(item));
        }

        Assert.Equal(0, (await bed.Sender.Send(new GetOutdatedAnalysisQuery(), Token)).Count);
    }

    private static HashSet<Id<MediaItem>> Set(params MediaItem[] items) => [.. items.Select(item => item.Id)];

    private Id<ModelRegistration>? ActiveModel => bed!.Library.Library.Settings.ActiveSearchModelId;

    private Domain.Libraries.Library WithThreshold(double threshold)
    {
        var library = bed!.Library.Library;
        library.ChangeSettings(library.Settings with { EmbeddingSwitchThreshold = threshold }, DateTimeOffset.UtcNow);
        return library;
    }

    private async Task<Id<ModelRegistration>> ModelAsync(FakeEmbeddingProvider provider) =>
        (await bed!.Library.Models.GetAllAsync(Token)).Single(model => model.ModelName == provider.Model.ModelName).Id;

    private async Task<IReadOnlyList<Id<ModelRegistration>>> ModelsOfAsync(MediaItem item) =>
        [.. (await bed!.Library.Embeddings.GetAsync(item.Id, Token)).Select(embedding => embedding.ModelId).Distinct()];

    private Task<MediaSearchResults> SearchAsync() => bed!.Sender.Send(new SearchMediaQuery { Text = Query }, Token);

    private async Task ReindexAsync(MediaItem item)
    {
        await bed!.Sender.Send(new AnalyzeItemsCommand([item.Id], [AnalyzerKeys.Embedding]), Token);
        await bed.WaitUntilIdleAsync();
    }

    private async Task<IReadOnlyList<MediaItem>> AnalyzedAsync(int count)
    {
        bed = await ImportTestBed.CreateAsync(configure: services => services
            .AddSingleton<IAiProviderRegistry>(new AiProviderRegistry([older, newer, describer], TimeProvider.System))
            .AddSearch());
        await bed.Get<SearchIndex>().Ready;
        bed.WriteDistinctPhotos(count);
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var items = (await bed.ItemsAsync()).OrderBy(item => item.Path.Value, StringComparer.Ordinal).ToList();
        await bed.Sender.Send(new AnalyzeItemsCommand([.. items.Select(item => item.Id)]), Token);
        await bed.WaitUntilIdleAsync();
        return items;
    }
}
