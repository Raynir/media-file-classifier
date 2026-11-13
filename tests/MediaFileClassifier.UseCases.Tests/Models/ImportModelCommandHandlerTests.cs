using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Testing.AI;
using MediaFileClassifier.UseCases.Models.ImportModel;

namespace MediaFileClassifier.UseCases.Tests.Models;

/// <summary>
/// Tests for <see cref="ImportModelCommandHandler"/>: picked files are matched to a catalogue tier by name and imported
/// with the catalogue's hashes.
/// </summary>
public sealed class ImportModelCommandHandlerTests
{
    private static readonly string Hash = new('a', 64);

    private readonly FakeStore store = new();
    private readonly ImportModelCommandHandler handler;

    public ImportModelCommandHandlerTests()
    {
        var entry = new ModelCatalogEntry(
            "siglip2",
            "1.0",
            "SigLIP 2",
            ModelPurpose.Embedding,
            "Apache-2.0",
            new Uri("https://example.com/license"),
            new Uri("https://example.com/siglip2"),
            "standard",
            [
                new ModelTier("compact", "Compact", [File("vision", "vision_model.onnx"), File("text", "text_model_fp16.onnx")]),
                new ModelTier("standard", "Standard", [File("vision", "vision_model.onnx"), File("text", "text_model.onnx")]),
            ]);
        handler = new ImportModelCommandHandler(new SingleEntryModelCatalog(entry), store);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllFilesOfBothTiers_PrefersTheDefaultTier_WithTheCataloguedHashes()
    {
        string[] paths = [@"C:\m\VISION_MODEL.onnx", @"C:\m\text_model.onnx", @"C:\m\text_model_fp16.onnx"];

        await handler.Handle(new ImportModelCommand(ModelPurpose.Embedding, paths, null), Token);

        var request = store.Request!;
        Assert.Equal(("siglip2", "1.0", "Apache-2.0"), (request.ModelId, request.Version, request.License));
        Assert.False(request.MoveSourceFiles);
        Assert.Equal([@"C:\m\VISION_MODEL.onnx", @"C:\m\text_model.onnx"], request.Files.Select(file => file.SourcePath));
        Assert.All(request.Files, file => Assert.Equal(Hash, file.ExpectedSha256));
    }

    [Fact]
    public async Task OnlyTheCompactTiersFiles_ImportsTheCompactTier()
    {
        await handler.Handle(new ImportModelCommand(ModelPurpose.Embedding, [@"C:\m\vision_model.onnx", @"C:\m\text_model_fp16.onnx"], null), Token);

        Assert.Equal("text_model_fp16.onnx", store.Request!.Files[1].FileName);
    }

    [Fact]
    public async Task MissingAFile_IsRejectedWithoutImporting()
    {
        await Assert.ThrowsAsync<ModelStoreException>(() => handler.Handle(new ImportModelCommand(ModelPurpose.Embedding, [@"C:\m\vision_model.onnx"], null), Token));

        Assert.Null(store.Request);
    }

    private static ModelCatalogFile File(string role, string name) => new(role, name, new Uri($"https://example.com/{name}"), 1, Hash);

    private sealed class FakeStore : IModelStore
    {
        public event EventHandler? ModelsChanged
        {
            add { }
            remove { }
        }

        public ModelImportRequest? Request { get; private set; }

        public string RootDirectory => @"C:\models";

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new InstalledModel(request.ModelId, request.Version, request.TierId, request.DisplayName, request.License, request.Source, RootDirectory, [], Hash, DateTimeOffset.UnixEpoch));
        }

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
