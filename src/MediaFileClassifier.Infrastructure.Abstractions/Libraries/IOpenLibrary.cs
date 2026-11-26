using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.Infrastructure.Abstractions.Libraries;

/// <summary>
/// A library this app instance has open. It holds the library's lock file until it's disposed.
/// Reads run in parallel; writes are queued and run one at a time (D072).
/// </summary>
public interface IOpenLibrary : IAsyncDisposable
{
    /// <summary>
    /// Gets the library's paths.
    /// </summary>
    LibraryLayout Layout { get; }

    /// <summary>
    /// Gets the library record as it was when the library was opened.
    /// </summary>
    Library Library { get; }

    /// <summary>
    /// Gets the indexed folders.
    /// </summary>
    IFolderRepository Folders { get; }

    /// <summary>
    /// Gets the media items.
    /// </summary>
    IMediaItemRepository Items { get; }

    /// <summary>
    /// Gets the items' descriptive values, tags and analysis runs.
    /// </summary>
    IItemMetadataRepository Metadata { get; }

    /// <summary>
    /// Gets the registered AI models.
    /// </summary>
    IModelRegistrationRepository Models { get; }

    /// <summary>
    /// Gets the processing jobs and their work items (E05-S01).
    /// </summary>
    IJobRepository Jobs { get; }

    /// <summary>
    /// Gets the embeddings (D067). Write them only through it, so the vector index stays current.
    /// </summary>
    IEmbeddingRepository Embeddings { get; }

    /// <summary>
    /// Gets the keyword index and the search filters (E01-S04). Search itself goes through <see cref="ISearchIndex"/>.
    /// </summary>
    ISearchStore Search { get; }

    /// <summary>
    /// Gets the keyframes sampled from videos and animated images (E06-S02).
    /// </summary>
    IKeyframeRepository Keyframes { get; }

    /// <summary>
    /// Gets the analyzer states of the library's items (E06-S01).
    /// </summary>
    IAnalyzerStateRepository AnalyzerStates { get; }

    /// <summary>
    /// Gets the activity log (E15-S04).
    /// </summary>
    IActivityRepository Activity { get; }

    /// <summary>
    /// Gets the duplicate groups (E12-S01).
    /// </summary>
    IDuplicateGroupRepository Duplicates { get; }

    /// <summary>
    /// Gets the visual near-duplicate queue and groups (E12-S02).
    /// </summary>
    IVisualDuplicateRepository VisualDuplicates { get; }

    /// <summary>
    /// Gets the integrity check, backup and optimization services.
    /// </summary>
    ILibraryMaintenance Maintenance { get; }

    /// <summary>
    /// Saves changes to the library record, such as its settings.
    /// </summary>
    /// <param name="library">The changed library record, with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task UpdateLibraryAsync(Library library, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the library's settings as one step: reads the current settings, applies <paramref name="change"/> and
    /// saves them, while no other settings change runs. Concurrent changes (an analysis profile save and an embedding
    /// model switch, say) therefore each keep the other's result. A failed save puts the previous settings back.
    /// Change settings only through this method.
    /// </summary>
    /// <param name="change">Returns the new settings from the current ones; it runs inside the step, so keep it quick and don't await in it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task UpdateSettingsAsync(Func<LibrarySettings, LibrarySettings> change, CancellationToken cancellationToken = default);
}
