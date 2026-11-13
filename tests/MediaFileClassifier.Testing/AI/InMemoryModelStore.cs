using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

namespace MediaFileClassifier.Testing.AI;

/// <summary>
/// A model store that keeps its models in memory and touches no files. Tests set <see cref="Installed"/>,
/// <see cref="Using"/> and <see cref="Problems"/> and read what was imported, deleted and recorded.
/// Deleting records the call and returns <c>Deleted: true</c>, but leaves <see cref="Installed"/> alone.
/// </summary>
public sealed class InMemoryModelStore : IModelStore
{
    /// <inheritdoc />
    public event EventHandler? ModelsChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets the installed models that the queries answer from.
    /// </summary>
    public List<InstalledModel> Installed { get; } = [];

    /// <summary>
    /// Gets or sets the names of the libraries <see cref="GetUsingLibrariesAsync"/> and delete report.
    /// </summary>
    public IReadOnlyList<string> Using { get; set; } = [];

    /// <summary>
    /// Gets or sets the problems <see cref="VerifyAsync"/> returns.
    /// </summary>
    public IReadOnlyList<string> Problems { get; set; } = [];

    /// <summary>
    /// Gets the last import request, or <see langword="null"/> before the first import.
    /// </summary>
    public ModelImportRequest? LastImport { get; private set; }

    /// <summary>
    /// Gets the number of <see cref="DeleteAsync"/> calls.
    /// </summary>
    public int Deletes { get; private set; }

    /// <summary>
    /// Gets the model id, version and tier of the last delete.
    /// </summary>
    public (string ModelId, string Version, string TierId)? DeletedTier { get; private set; }

    /// <summary>
    /// Gets or sets an action that runs on every delete, e.g. to make the test see the call order.
    /// </summary>
    public Action OnDelete { get; set; } = () => { };

    /// <summary>
    /// Gets the usage records: model id, version, content hash and library path.
    /// </summary>
    public List<(string ModelId, string Version, string ContentHash, string LibraryPath)> Usages { get; } = [];

    /// <inheritdoc />
    public string RootDirectory => @"C:\models";

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledModel>>([.. Installed]);

    /// <inheritdoc />
    public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
        Task.FromResult(Installed.FirstOrDefault(model => model.ModelId == modelId && model.Version == version && model.TierId == tierId));

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledModel>>([.. Installed.Where(model => model.ModelId == modelId && model.Version == version)]);

    /// <inheritdoc />
    public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        LastImport = request;
        var model = new InstalledModel(
            request.ModelId,
            request.Version,
            request.TierId,
            request.DisplayName,
            request.License,
            request.Source,
            RootDirectory,
            [],
            new string('a', 64),
            DateTimeOffset.UnixEpoch);
        Installed.Add(model);
        return Task.FromResult(model);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
        Task.FromResult(Problems);

    /// <inheritdoc />
    public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken)
    {
        Usages.Add((modelId, version, contentHash, libraryPath));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
        Task.FromResult(Using);

    /// <inheritdoc />
    public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken)
    {
        OnDelete();
        Deletes++;
        DeletedTier = (modelId, version, tierId);
        return Task.FromResult(new ModelDeletionResult(Deleted: true, Using));
    }
}
