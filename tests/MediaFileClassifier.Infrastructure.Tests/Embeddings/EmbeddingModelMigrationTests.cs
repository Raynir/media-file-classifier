using System.Diagnostics;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.UseCases.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.Embeddings;

/// <summary>
/// Tests for <see cref="EmbeddingModelMigration"/> (E06-S07-T05): the switch rule, the threshold, setting the active
/// search model of an existing library, and the cleanup at the switch, against a real library.
/// </summary>
public sealed class EmbeddingModelMigrationTests : IAsyncLifetime
{
    private readonly FakeEmbeddingProvider older = new();
    private readonly FakeEmbeddingProvider newer = new(modelName: "fake-embedder-2");
    private LibraryTestBed bed = null!;
    private ModelRegistration oldModel = null!;
    private ModelRegistration newModel = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        bed = await LibraryTestBed.CreateAsync();
        oldModel = await bed.Library.Models.GetOrAddAsync(older.Model, LibraryTestBed.Start, Token);
        newModel = await bed.Library.Models.GetOrAddAsync(newer.Model, LibraryTestBed.Start.AddDays(1), Token);

        // Analysis embeds with the new model.
        await ChangeSettingsAsync(settings => settings with
        {
            AnalysisProfile = settings.AnalysisProfile.With(new AnalyzerSettings(AnalyzerKeys.Embedding) { ModelId = newer.Model.ModelName }),
        });
    }

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Theory]
    [InlineData(100, 94, 0.95, false)]
    [InlineData(100, 95, 0.95, true)]
    [InlineData(4, 2, 0.5, true)]
    [InlineData(4, 1, 0.5, false)]
    [InlineData(0, 0, 0.95, true)]
    public void IsReady_ComparesTheShareWithTheThreshold(int withOld, int withBoth, double threshold, bool expected) =>
        Assert.Equal(expected, EmbeddingModelMigration.IsReady(new EmbeddingMigrationProgress(withOld, withBoth), threshold));

    [Fact]
    public async Task Opening_WithoutActiveModel_AndVectorsOfOneModel_MakesThatModelActive()
    {
        var items = await AddItemsAsync(3, withOld: 3, withNew: 0);

        using var migration = await OpenAsync(older, newer);

        Assert.Equal(oldModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal([oldModel.Id], await ModelsOfAsync(items[0]));
    }

    [Fact]
    public async Task Opening_WithoutActiveModel_AndVectorsOfSeveralModels_MakesTheCurrentModelActive()
    {
        var items = await AddItemsAsync(3, withOld: 3, withNew: 1);

        using var migration = await OpenAsync(older, newer);

        Assert.Equal(newModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal([newModel.Id], await ModelsOfAsync(items[0]));
        Assert.Equal([oldModel.Id], await ModelsOfAsync(items[1]));
    }

    [Fact]
    public async Task Check_BelowTheThreshold_KeepsTheOldModel_AndSwitchesOnceItsReached()
    {
        var items = await AddItemsAsync(20, withOld: 20, withNew: 18);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id });
        using var migration = await OpenAsync(older, newer);

        Assert.Equal(oldModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal([newModel.Id, oldModel.Id], (await ModelsOfAsync(items[0])).OrderBy(id => id == oldModel.Id));

        await EmbedAsync(items[18], newModel);
        Assert.True(await migration.CheckAsync(Token));

        Assert.Equal(newModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal([newModel.Id], await ModelsOfAsync(items[0]));
        Assert.Equal([oldModel.Id], await ModelsOfAsync(items[19]));
    }

    [Fact]
    public async Task Opening_WithALowerLibraryThreshold_SwitchesEarlier()
    {
        await AddItemsAsync(4, withOld: 4, withNew: 2);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id, EmbeddingSwitchThreshold = 0.5 });

        using var migration = await OpenAsync(older, newer);

        Assert.Equal(newModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
    }

    [Fact]
    public async Task Opening_OldModelNotInstalled_SwitchesAtOnce()
    {
        var items = await AddItemsAsync(4, withOld: 4, withNew: 1);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id });

        using var migration = await OpenAsync(newer);

        Assert.Equal(newModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal([newModel.Id], await ModelsOfAsync(items[0]));
        Assert.Equal([oldModel.Id], await ModelsOfAsync(items[1]));
    }

    [Fact]
    public async Task Check_OldModelNotInstalled_ButNoNewVectorsYet_KeepsTheOldModel()
    {
        await AddItemsAsync(2, withOld: 2, withNew: 0);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id });
        using var migration = await OpenAsync(newer);

        Assert.False(await migration.CheckAsync(Token));

        Assert.Equal(oldModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
    }

    [Fact]
    public async Task Settings_ActiveModelAndThreshold_AreKeptInTheLibrary()
    {
        Assert.Equal(LibrarySettings.DefaultEmbeddingSwitchThreshold, bed.Library.Library.Settings.EmbeddingSwitchThreshold);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id, EmbeddingSwitchThreshold = 0.8 });

        await bed.ReopenAsync();

        Assert.Equal(oldModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);
        Assert.Equal(0.8, bed.Library.Library.Settings.EmbeddingSwitchThreshold);
    }

    [Fact]
    public async Task NewModelVectors_Written_AreCheckedAfterTheDelay()
    {
        var items = await AddItemsAsync(2, withOld: 2, withNew: 1);
        await ChangeSettingsAsync(settings => settings with { ActiveSearchModelId = oldModel.Id });
        using var migration = await OpenAsync(TimeSpan.FromMilliseconds(50), older, newer);
        Assert.Equal(oldModel.Id, bed.Library.Library.Settings.ActiveSearchModelId);

        await EmbedAsync(items[1], newModel);

        var stopwatch = Stopwatch.StartNew();
        while (bed.Library.Library.Settings.ActiveSearchModelId != newModel.Id)
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "The check after the write never switched.");
            await Task.Delay(20, Token);
        }

        await migration.Pending;
        Assert.Equal([newModel.Id], await ModelsOfAsync(items[0]));
    }

    private Task<EmbeddingModelMigration> OpenAsync(params IAiProvider[] providers) => OpenAsync(TimeSpan.FromHours(1), providers);

    // The migration checks the open library when it's created; this waits for that check.
    private async Task<EmbeddingModelMigration> OpenAsync(TimeSpan delay, params IAiProvider[] providers)
    {
        var migration = new EmbeddingModelMigration(new AiProviderRegistry(providers, bed.Clock), new TestSession(bed.Library), bed.Clock, delay: delay);
        await migration.Pending;
        return migration;
    }

    private async Task ChangeSettingsAsync(Func<LibrarySettings, LibrarySettings> change)
    {
        bed.Library.Library.ChangeSettings(change(bed.Library.Library.Settings), bed.Clock.GetUtcNow());
        await bed.Library.UpdateLibraryAsync(bed.Library.Library, Token);
    }

    private async Task<IReadOnlyList<MediaItem>> AddItemsAsync(int count, int withOld, int withNew)
    {
        var items = new List<MediaItem>();
        for (var i = 0; i < count; i++)
        {
            var item = bed.NewItem(i + 1);
            await bed.Library.Items.AddAsync(item, Token);
            if (i < withOld)
            {
                await EmbedAsync(item, oldModel);
            }

            if (i < withNew)
            {
                await EmbedAsync(item, newModel);
            }

            items.Add(item);
        }

        return items;
    }

    private Task EmbedAsync(MediaItem item, ModelRegistration model) =>
        bed.Library.Embeddings.ReplaceAsync(
            item.Id,
            model.Id,
            [Embedding.Create(item.Id, model, EmbeddingKind.Image, FakeEmbeddingProvider.VectorFor(item.Id.ToString(), 8), LibraryTestBed.Start)],
            Token);

    private async Task<IReadOnlyList<Domain.Identity.Id<ModelRegistration>>> ModelsOfAsync(MediaItem item) =>
        [.. (await bed.Library.Embeddings.GetAsync(item.Id, Token)).Select(embedding => embedding.ModelId).Distinct()];
}
