using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>A model store that finds the SigLIP 2 model when <see cref="Installed"/> is set.</summary>
/// <param name="installed">The installed model, or null.</param>
internal sealed class Siglip2StubStore(InstalledModel? installed) : IModelStore
{
    public event EventHandler? ModelsChanged
    {
        add { }
        remove { }
    }

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
