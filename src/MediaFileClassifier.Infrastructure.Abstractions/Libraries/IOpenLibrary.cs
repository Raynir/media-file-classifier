using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

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
    /// Gets the integrity check, backup and optimization services.
    /// </summary>
    ILibraryMaintenance Maintenance { get; }
}
