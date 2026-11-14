using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.UseCases.Models.DeleteModel;
using MediaFileClassifier.UseCases.Models.DownloadModel;
using MediaFileClassifier.UseCases.Models.GetModels;

namespace MediaFileClassifier.UseCases.Tests.Models;

/// <summary>
/// Tests for the model management use cases (E17-S08-T01): the list of catalogue tiers and installed models, the
/// download of a chosen tier, and deletion that asks while libraries use a model and releases loaded models first.
/// </summary>
public sealed class ModelManagementHandlerTests
{
    private static readonly ModelCatalogEntry Entry = new(
        "siglip2",
        "1.0",
        "SigLIP 2",
        ModelPurpose.Embedding,
        "Apache-2.0",
        new Uri("https://example.com/license"),
        new Uri("https://example.com/siglip2"),
        "standard",
        [
            new ModelTier("standard", "Standard", [File("vision", 300), File("text", 200)]),
            new ModelTier("compact", "Compact", [File("vision", 300), File("text", 100)]) { Precision = "fp16-text" },
        ]);

    private readonly InMemoryModelStore store = new();
    private readonly SingleEntryModelCatalog catalog = new(Entry);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetModels_ListsEveryCatalogueTier_ThenUnknownInstalledModels()
    {
        store.Installed.Add(Installed("siglip2", "compact", size: 410));
        store.Installed.Add(Installed("custom", "standard", size: 7));

        var list = await new GetModelsQueryHandler(catalog, store).Handle(new GetModelsQuery(), Token);

        Assert.Equal(["standard", "compact", "standard"], list.Select(entry => entry.TierId));
        var standard = list[0];
        Assert.False(standard.IsInstalled);
        Assert.True(standard.IsDefaultTier);
        Assert.Equal((500L, "fp32", "Apache-2.0"), (standard.Size, standard.Precision, standard.License));
        var compact = list[1];
        Assert.True(compact.IsInstalled);
        Assert.Equal((410L, "fp16-text", "Compact"), (compact.Size, compact.Precision, compact.TierDisplayName));
        var custom = list[2];
        Assert.Equal(("custom", true, false), (custom.ModelId, custom.IsInstalled, custom.IsInCatalogue));
        Assert.Null(custom.Precision);
    }

    [Theory]
    [InlineData(null, "standard")]
    [InlineData("compact", "compact")]
    public async Task Download_GetsTheChosenTier_OrTheDefault(string? tierId, string expected)
    {
        var downloader = new FakeDownloader();

        await new DownloadModelCommandHandler(catalog, downloader).Handle(new DownloadModelCommand(ModelPurpose.Embedding, null) { TierId = tierId }, Token);

        Assert.Equal(expected, downloader.TierId);
    }

    [Fact]
    public async Task Download_UnknownTier_Throws()
    {
        var handler = new DownloadModelCommandHandler(catalog, new FakeDownloader());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.Handle(new DownloadModelCommand(ModelPurpose.Embedding, null) { TierId = "huge" }, Token));
    }

    [Fact]
    public async Task Delete_UsedByLibrariesAndNotConfirmed_DeletesNothingAndKeepsTheModelLoaded()
    {
        store.Using = [@"C:\Photos.mfclib"];
        var provider = new FakePreloadable();

        var result = await new DeleteModelCommandHandler(store, new FakeRegistry(provider)).Handle(new DeleteModelCommand("siglip2", "1.0", "standard", Confirmed: false), Token);

        Assert.True(result.NeedsConfirmation);
        Assert.Equal([@"C:\Photos.mfclib"], result.UsingLibraries);
        Assert.Equal(0, store.Deletes);
        Assert.Equal(0, provider.Unloads);
    }

    [Fact]
    public async Task Delete_Confirmed_UnloadsLoadedModelsFirst_ThenDeletes()
    {
        store.Using = [@"C:\Photos.mfclib"];
        var provider = new FakePreloadable();
        store.OnDelete = () => Assert.Equal(1, provider.Unloads);

        var result = await new DeleteModelCommandHandler(store, new FakeRegistry(provider)).Handle(new DeleteModelCommand("siglip2", "1.0", "compact", Confirmed: true), Token);

        Assert.True(result.Deleted);
        Assert.Equal(("siglip2", "1.0", "compact"), store.DeletedTier);
    }

    private static ModelCatalogFile File(string role, long size) =>
        new(role, role + ".onnx", new Uri("https://example.com/" + role), size, new string('a', 64));

    private static InstalledModel Installed(string id, string tier, long size) =>
        new(id, "1.0", tier, id, "MIT", null, @"C:\models", [new InstalledModelFile("vision", "vision.onnx", size, new string('a', 64))], "hash", DateTimeOffset.UnixEpoch);

    private sealed class FakeDownloader : IModelDownloader
    {
        public string? TierId { get; private set; }

        public Task<InstalledModel> DownloadAsync(ModelCatalogEntry entry, string tierId, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            TierId = tierId;
            return Task.FromResult(Installed(entry.ModelId, tierId, 1));
        }
    }

    private sealed class FakePreloadable : IPreloadableAiProvider
    {
        public int Unloads { get; private set; }

        public string Id => "fake";

        public string DisplayName => "Fake";

        public AiProviderKind Kind => AiProviderKind.Onnx;

        public ModelIdentity Model => throw new NotSupportedException();

        public bool IsSupportedOnCurrentPlatform => true;

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PreloadAsync(AiCapability? capability, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Unload() => Unloads++;
    }

    private sealed class FakeRegistry(IAiProvider provider) : IAiProviderRegistry
    {
        public IReadOnlyList<IAiProvider> Providers => [provider];

        public void Register(IAiProvider provider) => throw new NotSupportedException();

        public bool Unregister(string providerId) => throw new NotSupportedException();

        public IAiProvider? Find(string providerId) => throw new NotSupportedException();

        public IReadOnlyList<TCapability> GetProviders<TCapability>()
            where TCapability : class, IAiProvider => throw new NotSupportedException();

        public ITextEmbeddingProvider? FindTextEmbedderFor(ModelIdentity model) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProviderStatus>> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProviderStatus> CheckHealthAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ProviderStatus? GetLastHealth(string providerId) => throw new NotSupportedException();
    }
}
