using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
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

    public IJobRepository Jobs { get; } = new FakeJobRepository();

    public IKeyframeRepository Keyframes => throw new NotSupportedException();

    public IAnalyzerStateRepository AnalyzerStates => throw new NotSupportedException();

    public MediaFileClassifier.Infrastructure.Abstractions.Repositories.IEmbeddingRepository Embeddings => throw new NotSupportedException();

    public MediaFileClassifier.Infrastructure.Abstractions.Search.ISearchStore Search => throw new NotSupportedException();

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

    public Task<int> MergeIntoAsync(IndexedFolder parent, Id<IndexedFolder> childId, string relativePath, CancellationToken cancellationToken = default)
    {
        Folders.RemoveAll(folder => folder.Id == childId);
        return Task.FromResult(0);
    }

    public Task<bool> DeleteAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
        => Task.FromResult(Folders.RemoveAll(folder => folder.Id == id) > 0);
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

/// <summary>
/// A job repository with no jobs.
/// </summary>
internal sealed class FakeJobRepository : IJobRepository
{
    public Task<bool> SetResultAsync(Id<ProcessingJob> jobId, string result, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> AddWorkItemsAsync(Id<ProcessingJob> jobId, IReadOnlyCollection<WorkItem> workItems, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ProcessingJob?> GetJobAsync(Id<ProcessingJob> id, CancellationToken cancellationToken = default) => Task.FromResult<ProcessingJob?>(null);

    public Task<IReadOnlyList<ProcessingJob>> GetUnfinishedJobsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProcessingJob>>([]);

    public Task<IReadOnlyList<WorkItem>> GetQueuedAsync(ResourcePool pool, int limit, IReadOnlyCollection<Id<WorkItem>> excluding, IReadOnlyCollection<string>? excludingStages = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<WorkItem>> GetQueuedForItemsAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<IReadOnlyList<WorkItemCount>> CountUnfinishedWorkAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkItemCount>>([]);

    public Task<WorkItem?> StartAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ProcessingJob> FinishAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> RequeueRunningAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> MarkWaitingForModelAsync(IReadOnlyCollection<string> stages, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> ResumeWaitingForModelAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// A file source that only compares Windows-style folder paths, and reaches every folder not in <see cref="Unavailable"/>.
/// </summary>
internal sealed class FakeFolderSource : IMediaFileSource
{
    public HashSet<string> Unavailable { get; } = [];

    public IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) => Task.FromResult(!Unavailable.Contains(folder.Id));

    public string? GetPathInside(FolderHandle folder, FolderHandle parent)
    {
        if (string.Equals(folder.Id, parent.Id, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var prefix = parent.Id.TrimEnd('\\') + "\\";
        return folder.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? folder.Id[prefix.Length..].Replace('\\', '/') : null;
    }
}
