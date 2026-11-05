using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.DataAccess.Database;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for the model registration repository: registering models once, and finding the items a model change
/// leaves outdated (E01-S05-T04).
/// </summary>
public sealed class ModelRegistrationRepositoryTests
{
    private static readonly ModelIdentity OldModel = new(ProviderIds.Onnx, "siglip2-base-patch16-256", "1", "aaaa", 4);
    private static readonly ModelIdentity NewModel = new(ProviderIds.Onnx, "siglip2-base-patch16-256", "2", "bbbb", 4);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOrAdd_SameIdentityTwice_RegistersOnce()
    {
        await using var bed = await LibraryTestBed.CreateAsync();

        var first = await bed.Library.Models.GetOrAddAsync(OldModel, LibraryTestBed.Start, Token);
        var second = await bed.Library.Models.GetOrAddAsync(new ModelIdentity(ProviderIds.Onnx, "siglip2-base-patch16-256", "1", "AAAA", 4), LibraryTestBed.Start.AddDays(1), Token);
        var newVersion = await bed.Library.Models.GetOrAddAsync(NewModel, LibraryTestBed.Start, Token);

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Id, newVersion.Id);
        Assert.Equal(3, (await bed.Library.Models.GetAllAsync(Token)).Count); // the test bed's model, old and new
    }

    [Fact]
    public async Task ModelChange_ItemsEmbeddedOnlyByTheOldModel_AreOutdated()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var oldModel = await bed.Library.Models.GetOrAddAsync(OldModel, LibraryTestBed.Start, Token);
        var reEmbedded = await AddItemAsync(bed, 1);
        var outdated = await AddItemAsync(bed, 2);
        var neverEmbedded = await AddItemAsync(bed, 3);
        var derivedTextOnly = await AddItemAsync(bed, 4);
        await AddEmbeddingsAsync(bed, Embedding.Create(reEmbedded.Id, oldModel, EmbeddingKind.Image, Vector, LibraryTestBed.Start));
        await AddEmbeddingsAsync(bed, Embedding.Create(outdated.Id, oldModel, EmbeddingKind.Image, Vector, LibraryTestBed.Start));
        await AddEmbeddingsAsync(bed, Embedding.Create(derivedTextOnly.Id, oldModel, EmbeddingKind.DerivedText, Vector, LibraryTestBed.Start));

        // The user switches to the new model, and one item is re-embedded so far.
        var newModel = await bed.Library.Models.GetOrAddAsync(NewModel, LibraryTestBed.Start, Token);
        await AddEmbeddingsAsync(bed, Embedding.Create(reEmbedded.Id, newModel, EmbeddingKind.Image, Vector, LibraryTestBed.Start));

        var items = await bed.Library.Models.GetItemsWithOutdatedEmbeddingsAsync(newModel.Id, EmbeddingKind.Image, Token);

        Assert.Equal([outdated.Id], items);
        Assert.DoesNotContain(neverEmbedded.Id, items);
    }

    [Fact]
    public async Task ModelChange_ItemsWithAnActiveRunOfTheOldModel_AreOutdated()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var oldModel = await bed.Library.Models.GetOrAddAsync(OldModel, LibraryTestBed.Start, Token);
        var newModel = await bed.Library.Models.GetOrAddAsync(NewModel, LibraryTestBed.Start, Token);
        var analyzedByOld = await AddItemAsync(bed, 1);
        var analyzedByNew = await AddItemAsync(bed, 2);
        var otherAnalyzer = await AddItemAsync(bed, 3);
        await ApplyRunAsync(bed, analyzedByOld, "caption", oldModel);
        await ApplyRunAsync(bed, analyzedByNew, "caption", oldModel);
        await ApplyRunAsync(bed, analyzedByNew, "caption", newModel);
        await ApplyRunAsync(bed, otherAnalyzer, "ocr", oldModel);

        var items = await bed.Library.Models.GetItemsWithOutdatedAnalysisAsync("caption", newModel.Id, Token);

        Assert.Equal([analyzedByOld.Id], items);
    }

    private static float[] Vector => [1f, 0f, 0f, 0f];

    private static async Task<MediaItem> AddItemAsync(LibraryTestBed bed, int number)
    {
        var item = bed.NewItem(number);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private static async Task AddEmbeddingsAsync(LibraryTestBed bed, params Embedding[] embeddings)
    {
        // No embedding repository yet (E06-S03); write through the context.
        await using var db = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(true, Token);
        db.Embeddings.AddRange(embeddings);
        await db.SaveChangesAsync(Token);
    }

    private static async Task ApplyRunAsync(LibraryTestBed bed, MediaItem item, string analyzer, ModelRegistration model)
    {
        var run = AnalysisRun.Start(item.Id, analyzer, model, "v1", bed.Clock.Tick());
        run.Complete(bed.Clock.Tick());
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.ApplyRun(run, [], [], 1), Token);
    }
}
