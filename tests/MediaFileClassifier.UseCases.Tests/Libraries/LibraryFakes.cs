using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Library locations whose answers the test sets.
/// </summary>
internal sealed class FakeLibraryLocations : ILibraryLocations
{
    public string DefaultParentDirectory => @"C:\Users\Test\Media File Classifier";

    public LibraryLocationInfo Info { get; set; } = new(IsWritable: true, FreeBytes: 10L << 30, IsInsideLibrary: false, LibraryExists: false);

    public HashSet<string> Missing { get; } = [];

    public LibraryLocationInfo Inspect(string parentDirectory, string libraryName) => Info;

    public bool IsAvailable(string libraryDirectory) => !Missing.Contains(libraryDirectory);
}

/// <summary>
/// A library store that creates fake open libraries, or throws what the test sets.
/// </summary>
internal sealed class FakeLibraryStore : ILibraryStore
{
    public Exception? Failure { get; set; }

    public List<(string Parent, string Name)> Created { get; } = [];

    public Task<IOpenLibrary> CreateAsync(string parentDirectory, string name, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<IOpenLibrary>(Failure);
        }

        Created.Add((parentDirectory, name));
        return Task.FromResult<IOpenLibrary>(new FakeOpenLibrary(LibraryLayout.For(parentDirectory, name), name));
    }

    public List<string> Opened { get; } = [];

    public List<(string Library, string Backup)> Restored { get; } = [];

    public Task<IOpenLibrary> OpenAsync(string libraryDirectory, CancellationToken cancellationToken = default)
        => OpenAsync(libraryDirectory, progress: null, cancellationToken);

    public Task<IOpenLibrary> OpenAsync(string libraryDirectory, IProgress<LibraryOpenStage>? progress, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<IOpenLibrary>(Failure);
        }

        Opened.Add(libraryDirectory);
        var layout = LibraryLayout.FromDirectory(libraryDirectory);
        return Task.FromResult<IOpenLibrary>(new FakeOpenLibrary(layout, Path.GetFileNameWithoutExtension(layout.RootDirectory)));
    }

    public Task RestoreBackupAsync(string libraryDirectory, string backupPath, CancellationToken cancellationToken = default)
    {
        Restored.Add((libraryDirectory, backupPath));
        return Task.CompletedTask;
    }
}

/// <summary>
/// An open library with an in-memory folder list.
/// </summary>
internal sealed class FakeOpenLibrary(LibraryLayout layout, string name) : IOpenLibrary
{
    public LibraryLayout Layout { get; } = layout;

    public Library Library { get; } = Library.Create(name, DateTimeOffset.UnixEpoch);

    public FakeFolderRepository FolderList { get; } = new();

    public IFolderRepository Folders => FolderList;

    public FakeMediaItemRepository ItemList { get; } = new();

    public IMediaItemRepository Items => ItemList;

    public IItemMetadataRepository Metadata => throw new NotSupportedException();

    public IModelRegistrationRepository Models => throw new NotSupportedException();

    public IJobRepository Jobs => throw new NotSupportedException();

    public ILibraryMaintenance Maintenance => throw new NotSupportedException();

    public bool IsDisposed { get; private set; }

    public int LibraryUpdates { get; private set; }

    public Task UpdateLibraryAsync(Library library, CancellationToken cancellationToken = default)
    {
        LibraryUpdates++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Folders kept in a list.
/// </summary>
internal sealed class FakeFolderRepository : IFolderRepository
{
    public List<IndexedFolder> Folders { get; } = [];

    public Task<IndexedFolder?> GetAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
        => Task.FromResult(Folders.FirstOrDefault(folder => folder.Id == id));

    public Task<IReadOnlyList<IndexedFolder>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IndexedFolder>>([.. Folders]);

    public Task AddAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        Folders.Add(folder);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IndexedFolder folder, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// A library session without side effects.
/// </summary>
internal sealed class FakeLibrarySession : ILibrarySession
{
    public event EventHandler? CurrentChanged;

    public IOpenLibrary? Current { get; private set; }

    public Task SetCurrentAsync(IOpenLibrary? library)
    {
        Current = library;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}

/// <summary>
/// App settings kept in memory.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public void Save(AppSettings settings) => Current = settings;

    public Exception? UpdateFailure { get; set; }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
        => UpdateFailure is { } failure ? throw failure : Current = change(Current);
}
