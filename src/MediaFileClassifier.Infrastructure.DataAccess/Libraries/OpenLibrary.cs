using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// A library this app instance has open: its lock, its writer and its repositories.
/// </summary>
internal sealed class OpenLibrary : IOpenLibrary
{
    private readonly LibraryConnectionFactory connections;
    private readonly LibraryWriter writer;
    private readonly LibraryLock heldLock;
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
        Folders = new FolderRepository(connections, writer);
        Items = new MediaItemRepository(connections, writer);
        Metadata = new ItemMetadataRepository(connections, writer, timeProvider);
        Models = new ModelRegistrationRepository(connections, writer);
        Jobs = new JobRepository(connections, writer);
        Keyframes = new KeyframeRepository(connections, writer);
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
