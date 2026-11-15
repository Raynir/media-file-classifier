using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Evaluation.Systems;

/// <summary>
/// A model store holding one model whose files sit in a plain folder (<c>--models</c>), so the app's providers run
/// on files that weren't installed through the catalogue. Hashes aren't checked.
/// </summary>
/// <param name="directory">The folder.</param>
/// <param name="modelId">The model ID the provider looks for.</param>
/// <param name="version">The model version the provider looks for.</param>
/// <param name="displayName">The model's name.</param>
/// <param name="files">The file names, by role.</param>
internal sealed class DirectoryModelStore(string directory, string modelId, string version, string displayName, IReadOnlyDictionary<string, string> files)
    : IModelStore
{
    private const string TierId = "standard";

    public event EventHandler? ModelsChanged
    {
        add { }
        remove { }
    }

    public string RootDirectory => directory;

    public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
        Task.FromResult<InstalledModel?>(IsThis(modelId, version) && tierId == TierId ? Installed() : null);

    public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledModel>>(IsThis(modelId, version) ? [Installed()] : []);

    public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken) => throw new NotSupportedException();

    private bool IsThis(string id, string ver) => id == modelId && ver == version;

    private InstalledModel Installed() => new(
        modelId,
        version,
        TierId,
        displayName,
        null,
        "evaluation",
        directory,
        [.. files.Select(file => new InstalledModelFile(file.Key, file.Value, 0, "0"))],
        "evaluation",
        DateTimeOffset.UnixEpoch);
}
