namespace MediaFileClassifier.Infrastructure.Abstractions.AI.Models;

/// <summary>
/// The per-user model store (D032): <c>%LOCALAPPDATA%\MediaFileClassifier\models\&lt;model-id&gt;\&lt;version&gt;\</c>,
/// shared by all libraries. Each installed model has a <c>manifest.json</c> listing its files with their sizes and
/// SHA-256 hashes (E01-S05-T06).
/// </summary>
/// <remarks>
/// <para>
/// Models arrive by manual import, or by the catalogue download (E06-S12), which downloads to a temporary file and
/// then calls <see cref="ImportAsync"/> with the publisher's hashes. Either way the hashes are checked before the model
/// becomes visible.
/// </para>
/// <para>
/// Libraries don't hold model files. They record which models they use with <see cref="RecordUsageAsync"/>, so that
/// <see cref="DeleteAsync"/> can name them before a model they use is removed.
/// </para>
/// </remarks>
public interface IModelStore
{
    /// <summary>
    /// Gets the store's root folder.
    /// </summary>
    string RootDirectory { get; }

    /// <summary>
    /// Lists the installed models. Folders without a readable manifest are skipped.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The models, by ID and version.</returns>
    Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds an installed model.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <param name="version">Model version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The model, or <c>null</c> if it isn't installed.</returns>
    Task<InstalledModel?> FindAsync(string modelId, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Installs a model: copies (or moves) its files into the store while hashing them, checks the hashes the caller
    /// expects, writes the manifest, and only then makes the model visible. Nothing is left behind if any step fails.
    /// </summary>
    /// <param name="request">What to install.</param>
    /// <param name="progress">Receives the number of bytes processed so far, if not <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed model.</returns>
    /// <exception cref="ModelStoreException">A source file is missing, a hash doesn't match, or the version is already installed.</exception>
    Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Hashes an installed model's files again and compares them with its manifest.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <param name="version">Model version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The problems found; empty if the model is intact.</returns>
    /// <exception cref="ModelStoreException">The model isn't installed.</exception>
    Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a library uses a model.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <param name="version">Model version.</param>
    /// <param name="libraryPath">The library's folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the usage is saved.</returns>
    Task RecordUsageAsync(string modelId, string version, string libraryPath, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the libraries that recorded using a model. A library that was moved or deleted since may still be listed.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <param name="version">Model version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The libraries' folders.</returns>
    Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a model from the store. If libraries recorded using it, nothing is deleted until the caller confirms,
    /// and the result names those libraries so the UI can warn first.
    /// </summary>
    /// <param name="modelId">Model ID.</param>
    /// <param name="version">Model version.</param>
    /// <param name="confirmed">Whether the user confirmed deleting a model that libraries use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the model was deleted, and the libraries that use it.</returns>
    /// <exception cref="ModelStoreException">The model isn't installed, or its files are in use.</exception>
    Task<ModelDeletionResult> DeleteAsync(string modelId, string version, bool confirmed, CancellationToken cancellationToken);
}
