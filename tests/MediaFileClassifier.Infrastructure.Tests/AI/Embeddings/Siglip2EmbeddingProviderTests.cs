using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

public sealed class Siglip2EmbeddingProviderTests
{
    [Fact]
    public async Task CheckHealth_ModelNotInstalled_IsModelMissing()
    {
        using var provider = new Siglip2EmbeddingProvider(new StubStore(null), new OnnxSessionFactory(), new Siglip2Options());

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.ModelMissing, health.Status);
        Assert.Null(provider.Model.FileHash);
        Assert.Equal(Siglip2Model.Dimension, provider.Model.EmbeddingDimension);
    }

    [Fact]
    public async Task Embed_ModelNotInstalled_ThrowsModelMissing()
    {
        using var provider = new Siglip2EmbeddingProvider(new StubStore(null), new OnnxSessionFactory(), new Siglip2Options());

        var text = await Assert.ThrowsAsync<AiProviderException>(() => provider.EmbedTextsAsync(["cat"], TestContext.Current.CancellationToken));
        var image = await Assert.ThrowsAsync<AiProviderException>(() => provider.EmbedImagesAsync([new AiImage(new byte[1], "image/png")], TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.ModelMissing, text.Kind);
        Assert.Equal(AiFailureKind.ModelMissing, image.Kind);
    }

    [Fact]
    public async Task CheckHealth_InstalledButFileDeleted_IsModelMissing()
    {
        var directory = Directory.CreateTempSubdirectory("mfc-siglip2-").FullName;
        try
        {
            using var provider = new Siglip2EmbeddingProvider(new StubStore(Siglip2TestModel.CreateInstalled(directory)), new OnnxSessionFactory(), new Siglip2Options());

            var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ProviderHealthStatus.ModelMissing, health.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ModelInstalledLater_IsPickedUpWithoutRestart()
    {
        var store = new StubStore(null);
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(), new Siglip2Options());
        Assert.Equal(ProviderHealthStatus.ModelMissing, (await provider.CheckHealthAsync(TestContext.Current.CancellationToken)).Status);

        store.Installed = Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory());

        Assert.Equal(ProviderHealthStatus.Healthy, (await provider.CheckHealthAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal("abc123", provider.Model.FileHash);
    }

    [Fact]
    public async Task Unload_ThenEmbed_LoadsAgainWithTheSameIdentity()
    {
        using var provider = new Siglip2EmbeddingProvider(
            new StubStore(Siglip2TestModel.CreateInstalled(Siglip2TestModel.RequireDirectory())), new OnnxSessionFactory(), new Siglip2Options());
        await provider.PreloadAsync(AiCapability.TextEmbedding, TestContext.Current.CancellationToken);
        var before = await provider.EmbedTextsAsync(["a red car"], TestContext.Current.CancellationToken);

        provider.Unload();
        var after = await provider.EmbedTextsAsync(["a red car"], TestContext.Current.CancellationToken);

        Assert.Equal(before.Model, after.Model);
        Assert.Equal(before.Value[0].ToArray(), after.Value[0].ToArray());
        Assert.Equal(Siglip2Model.ProviderId, after.ProviderId);
    }

    private sealed class StubStore(InstalledModel? installed) : IModelStore
    {
        public InstalledModel? Installed { get; set; } = installed;

        public string RootDirectory => "models";

        public Task<InstalledModel?> FindAsync(string modelId, string version, CancellationToken cancellationToken) =>
            Task.FromResult(modelId == Siglip2Model.ModelId && version == Siglip2Model.Version ? Installed : null);

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordUsageAsync(string modelId, string version, string libraryPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
