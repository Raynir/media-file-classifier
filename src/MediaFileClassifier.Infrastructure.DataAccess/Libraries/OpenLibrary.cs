using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Search;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// A library this app instance has open: its lock, its writer and its repositories.
/// </summary>
[SuppressMessage(
    "Usage",
    "CA2213:Disposable fields should be disposed",
    Justification = "A settings change may still wait on or release the gate while the library closes; the semaphore holds no handle that needs disposing.")]
internal sealed class OpenLibrary : IOpenLibrary
{
    private readonly LibraryConnectionFactory connections;
    private readonly LibraryWriter writer;
    private readonly LibraryLock heldLock;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim settingsGate = new(1, 1);
    private int disposed;

    private OpenLibrary(
        LibraryLayout layout,
        Library library,
        LibraryConnectionFactory connections,
        LibraryWriter writer,
        LibraryLock heldLock,
        TimeProvider timeProvider)
    {
        Layout = layout;
        Library = library;
        this.connections = connections;
        this.writer = writer;
        this.heldLock = heldLock;
        this.timeProvider = timeProvider;
        Folders = new FolderRepository(connections, writer);
        Items = new MediaItemRepository(connections, writer);
        Metadata = new ItemMetadataRepository(connections, writer, timeProvider);
        Models = new ModelRegistrationRepository(connections, writer);
        Jobs = new JobRepository(connections, writer);
        Keyframes = new KeyframeRepository(connections, writer);
        AnalyzerStates = new AnalyzerStateRepository(connections, writer);
        Embeddings = new EmbeddingRepository(connections, writer);
        Search = new SearchStore(connections, writer);
        Maintenance = new LibraryMaintenance(connections, writer);
    }

    /// <inheritdoc />
    public LibraryLayout Layout { get; }

    /// <inheritdoc />
    public Library Library { get; }

    /// <inheritdoc />
    public IFolderRepository Folders { get; }

    /// <inheritdoc />
    public IMediaItemRepository Items { get; }

    /// <inheritdoc />
    public IItemMetadataRepository Metadata { get; }

    /// <inheritdoc />
    public IModelRegistrationRepository Models { get; }

    /// <inheritdoc />
    public IJobRepository Jobs { get; }

    /// <inheritdoc />
    public IKeyframeRepository Keyframes { get; }

    /// <inheritdoc />
    public IAnalyzerStateRepository AnalyzerStates { get; }

    /// <inheritdoc />
    public IEmbeddingRepository Embeddings { get; }

    /// <inheritdoc />
    public ISearchStore Search { get; }

    /// <inheritdoc />
    public ILibraryMaintenance Maintenance { get; }

    /// <summary>
    /// Starts the writer of a library whose lock is held and whose schema is current.
    /// </summary>
    /// <param name="layout">The library's paths.</param>
    /// <param name="library">The library record.</param>
    /// <param name="connections">Connection factory of the library.</param>
    /// <param name="heldLock">The library's lock, which the open library now owns.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open library.</returns>
    public static async Task<OpenLibrary> StartAsync(
        LibraryLayout layout,
        Library library,
        LibraryConnectionFactory connections,
        LibraryLock heldLock,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var writer = await LibraryWriter.StartAsync(connections, cancellationToken).ConfigureAwait(false);
        return new OpenLibrary(layout, library, connections, writer, heldLock, timeProvider);
    }

    /// <inheritdoc />
    public Task UpdateLibraryAsync(Library library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        return writer.ExecuteAsync(
            async (db, token) =>
            {
                db.Libraries.Update(library);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateSettingsAsync(Func<LibrarySettings, LibrarySettings> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        // The settings live in one shared record: reading them, changing them and writing them back has to be one step,
        // or two changes that start together would each overwrite the other (MFC-998).
        await settingsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Library.Settings;
            Library.ChangeSettings(change(previous), timeProvider.GetUtcNow());
            try
            {
                await UpdateLibraryAsync(Library, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Keep what the library file still holds.
                Library.ChangeSettings(previous, timeProvider.GetUtcNow());
                throw;
            }
        }
        finally
        {
            settingsGate.Release();
        }
    }

    /// <summary>
    /// Waits for the queued writes, closes every connection, which checkpoints the WAL into the database file, and
    /// releases the lock.
    /// </summary>
    /// <returns>A task that completes when the library is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await writer.DisposeAsync().ConfigureAwait(false);
        connections.Close();
        heldLock.Dispose();
    }
}
