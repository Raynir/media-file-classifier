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

    public List<LibraryBackup> Backups { get; } = [];

    public IReadOnlyList<LibraryBackup> GetBackups(string libraryDirectory) => Backups;

    public Task<IntegrityCheckResult> CheckBackupAsync(string backupPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new IntegrityCheckResult([]));

    public Task<string> ReplaceDamagedDatabaseAsync(string libraryDirectory, string backupPath, CancellationToken cancellationToken = default)
    {
        Restored.Add((libraryDirectory, backupPath));
        return Task.FromResult(Path.Combine(libraryDirectory, LibraryLayout.BackupsFolderName, "library-damaged.db"));
    }

    public Task PutBackDamagedDatabaseAsync(string libraryDirectory, string damagedPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
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

    public IItemMetadataRepository MetadataRepository { get; set; } = null!;

    public IItemMetadataRepository Metadata => MetadataRepository ?? throw new NotSupportedException();

    public IModelRegistrationRepository Models => throw new NotSupportedException();

    public FakeJobRepository JobList { get; } = new();

    public IJobRepository Jobs => JobList;

    public IKeyframeRepository Keyframes => KeyframeList;

    public FakeKeyframeRepository KeyframeList { get; } = new();

    public IAnalyzerStateRepository? AnalyzerStateList { get; set; }

    public IAnalyzerStateRepository AnalyzerStates => AnalyzerStateList ?? throw new NotSupportedException();

    public IEmbeddingRepository? EmbeddingList { get; set; }

    public IEmbeddingRepository Embeddings => EmbeddingList ?? throw new NotSupportedException();

    public MediaFileClassifier.Infrastructure.Abstractions.Search.ISearchStore Search => throw new NotSupportedException();

    public ILibraryMaintenance? MaintenanceService { get; set; }

    public ILibraryMaintenance Maintenance => MaintenanceService ?? throw new NotSupportedException();

    public bool IsDisposed { get; private set; }

    public int LibraryUpdates { get; private set; }

    public Task UpdateLibraryAsync(Library library, CancellationToken cancellationToken = default)
    {
        LibraryUpdates++;
        return Task.CompletedTask;
    }

    public Task UpdateSettingsAsync(Func<LibrarySettings, LibrarySettings> change, CancellationToken cancellationToken = default)
    {
        Library.ChangeSettings(change(Library.Settings), DateTimeOffset.UnixEpoch);
        return UpdateLibraryAsync(Library, cancellationToken);
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
    // The status each folder was last stored with, so StatusChanged is raised like the real repository's.
    private readonly Dictionary<Id<IndexedFolder>, FolderStatus> stored = [];

    public event EventHandler? StatusChanged;

    public event EventHandler? Changed;

    public List<IndexedFolder> Folders { get; } = [];

    public Task<IndexedFolder?> GetAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
        => Task.FromResult(Folders.FirstOrDefault(folder => folder.Id == id));

    public Task<IReadOnlyList<IndexedFolder>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IndexedFolder>>([.. Folders]);

    public Task AddAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        Folders.Add(folder);
        stored[folder.Id] = folder.Status;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public int Updates { get; private set; }

    public int AvailabilityUpdates { get; private set; }

    public Task UpdateAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        Updates++;
        Store(folder);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task UpdateAvailabilityAsync(IndexedFolder folder, CancellationToken cancellationToken = default)
    {
        Updates++;
        AvailabilityUpdates++;
        Store(folder);
        return Task.CompletedTask;
    }

    // Changed is raised like the real repository's: after every add, update, merge and split, and after a delete that
    // removed the folder.
    public Task<int> MergeIntoAsync(IndexedFolder parent, Id<IndexedFolder> childId, string relativePath, CancellationToken cancellationToken = default)
    {
        Folders.RemoveAll(folder => folder.Id == childId);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(0);
    }

    public Task<(int Moved, bool PartialDeleted)> SplitIntoAsync(IndexedFolder folder, Id<IndexedFolder> partialId, string relativePath, CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult((0, false));
    }

    public Task<bool> DeleteAsync(Id<IndexedFolder> id, CancellationToken cancellationToken = default)
    {
        var deleted = Folders.RemoveAll(folder => folder.Id == id) > 0;
        if (deleted)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(deleted);
    }

    private void Store(IndexedFolder folder)
    {
        var changed = stored.TryGetValue(folder.Id, out var old) && old != folder.Status;
        stored[folder.Id] = folder.Status;
        if (changed)
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
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
/// A job repository whose jobs and counts the test sets.
/// </summary>
internal sealed class FakeJobRepository : IJobRepository
{
    public Task<bool> SetResultAsync(Id<ProcessingJob> jobId, string result, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task AddJobAsync(ProcessingJob job, IReadOnlyCollection<WorkItem> workItems, Action<IReadOnlyCollection<WorkItem>>? stored, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> AddWorkItemsAsync(Id<ProcessingJob> jobId, IReadOnlyCollection<WorkItem> workItems, DateTimeOffset now, Action<IReadOnlyCollection<WorkItem>>? stored, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>
    /// Gets or sets the state every job is in, for <see cref="GetJobAsync"/>; <see langword="null"/> for no jobs.
    /// </summary>
    public ProcessingJobState? JobState { get; set; }

    public Task<ProcessingJob?> GetJobAsync(Id<ProcessingJob> id, CancellationToken cancellationToken = default) =>
        Task.FromResult(JobState is { } jobState
            ? new ProcessingJob { Id = id, Type = "analysis", Description = "Analyze 1 item", State = jobState, CreatedAt = DateTimeOffset.UnixEpoch }
            : null);

    public Task<IReadOnlyList<ProcessingJob>> GetUnfinishedJobsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProcessingJob>>([.. Unfinished]);

    /// <summary>
    /// Gets the jobs <see cref="GetUnfinishedJobsAsync"/> returns.
    /// </summary>
    public List<ProcessingJob> Unfinished { get; } = [];

    /// <summary>
    /// Gets the jobs <see cref="GetRecentJobsAsync"/> returns, newest first.
    /// </summary>
    public List<ProcessingJob> Recent { get; } = [];

    /// <summary>
    /// Gets the work item counts <see cref="CountWorkAsync"/> returns for the jobs asked for.
    /// </summary>
    public List<WorkItemCount> Counts { get; } = [];

    /// <summary>
    /// Gets the failures <see cref="CountFailuresAsync"/> returns.
    /// </summary>
    public Dictionary<Id<ProcessingJob>, int> Failures { get; } = [];

    public Task<IReadOnlyList<ProcessingJob>> GetRecentJobsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProcessingJob>>([.. Recent.Take(limit)]);

    public Task<IReadOnlyList<WorkItemCount>> CountWorkAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkItemCount>>([.. Counts.Where(count => jobIds.Contains(count.JobId))]);

    public Task<IReadOnlyDictionary<Id<ProcessingJob>, int>> CountFailuresAsync(IReadOnlyCollection<Id<ProcessingJob>> jobIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Id<ProcessingJob>, int>>(Failures);

    /// <summary>
    /// Gets the work items <see cref="GetFailedWorkItemsAsync"/> returns.
    /// </summary>
    public List<WorkItem> FailedWork { get; } = [];

    public Task<IReadOnlyList<WorkItem>> GetFailedWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkItem>>([.. FailedWork.Where(item => item.JobId == jobId)]);

    public Task<IReadOnlyList<WorkItem>> GetQueuedAsync(ResourcePool pool, int limit, IReadOnlyCollection<Id<WorkItem>> excluding, IReadOnlyCollection<string>? excludingStages = null, DateTimeOffset? dueBy = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<DateTimeOffset?> GetNextRetryAtAsync(DateTimeOffset after, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> ReleaseParkedAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<WorkItem>> GetQueuedForItemsAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(Id<ProcessingJob> jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    public Task<IReadOnlyList<WorkItemCount>> CountUnfinishedWorkAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkItemCount>>([]);

    public Task<WorkItem?> StartAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ProcessingJob> FinishAsync(WorkItem workItem, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> CancelJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> PauseJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> ResumeJobAsync(Id<ProcessingJob> jobId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> ResumeAllJobsAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> RequeueRunningAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> PruneFinishedJobsAsync(int keepLast, DateTimeOffset olderThan, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> MarkWaitingForModelAsync(IReadOnlyCollection<string> stages, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> ResumeWaitingForModelAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// A file source that only compares Windows-style folder paths, and reaches every folder not in <see cref="Unavailable"/>.
/// </summary>
internal sealed class FakeFolderSource : IMediaFileSource
{
    public HashSet<string> Unavailable { get; } = [];

    /// <summary>
    /// Gets the volume label of each folder, by its handle.
    /// </summary>
    public Dictionary<string, string?> Labels { get; } = [];

    public IAsyncEnumerable<MediaFileStat> EnumerateFilesAsync(FolderHandle folder, string relativeDirectory = "", CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<FolderScanEntry> ScanAsync(FolderHandle folder, FolderScanOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Stream> OpenReadAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<MediaFileStat?> GetStatAsync(FileLocation file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>
    /// Gets the folders whose check never ends and ignores its token, like an unreachable network share before the SMB
    /// timeout.
    /// </summary>
    public HashSet<string> Hanging { get; } = [];

    public Task<bool> IsAvailableAsync(FolderHandle folder, CancellationToken cancellationToken = default) =>
        Hanging.Contains(folder.Id) ? new TaskCompletionSource<bool>().Task : Task.FromResult(!Unavailable.Contains(folder.Id));

    /// <summary>
    /// Gets the folders whose volume can't be asked for its label.
    /// </summary>
    public HashSet<string> UnreadableLabels { get; } = [];

    public Task<string?> GetVolumeLabelAsync(FolderHandle folder, CancellationToken cancellationToken = default) =>
        UnreadableLabels.Contains(folder.Id)
            ? Task.FromException<string?>(new IOException("The device is not ready."))
            : Task.FromResult(Labels.GetValueOrDefault(folder.Id));

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

/// <summary>
/// Keeps keyframes per item in memory.
/// </summary>
internal sealed class FakeKeyframeRepository : IKeyframeRepository
{
    public Dictionary<Id<MediaItem>, IReadOnlyList<Keyframe>> Keyframes { get; } = [];

    public Task<IReadOnlyList<Keyframe>> GetForItemAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Keyframes.TryGetValue(itemId, out var keyframes) ? keyframes : (IReadOnlyList<Keyframe>)[]);

    public Task ReplaceAsync(Id<MediaItem> itemId, IReadOnlyList<Keyframe> keyframes, CancellationToken cancellationToken = default)
    {
        Keyframes[itemId] = keyframes;
        return Task.CompletedTask;
    }
}
