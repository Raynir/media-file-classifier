using System.Collections.Concurrent;
using System.Diagnostics;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.Discovery;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Hashing;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Capabilities;
using MediaFileClassifier.Infrastructure.Discovery;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Infrastructure.Hashing;
using MediaFileClassifier.Infrastructure.Jobs;
using MediaFileClassifier.Infrastructure.Media;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Media.Thumbnails;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Import;
using MediaFileClassifier.UseCases.Import.ImportFolders;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// A real library, a real folder on disk, and the import pipeline wired as the app wires it: discovery, hashing,
/// metadata, thumbnails and the job system (E03-S07).
/// </summary>
internal sealed class ImportTestBed : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private bool ownsRoot;

    private ImportTestBed(LibraryTestBed library, string root, IndexedFolder folder, ServiceProvider services, ImportTestSettings settings)
    {
        LibraryBed = library;
        Root = root;
        Folder = folder;
        this.services = services;
        Settings = settings;
    }

    public LibraryTestBed LibraryBed { get; }

    public IOpenLibrary Library => LibraryBed.Library;

    public string Root { get; }

    public IndexedFolder Folder { get; }

    public ImportTestSettings Settings { get; }

    public IJobSystem Jobs => services.GetRequiredService<IJobSystem>();

    public ISender Sender => services.GetRequiredService<ISender>();

    public RegisteredRecorder Registered => services.GetRequiredService<RegisteredRecorder>();

    public T Get<T>()
        where T : notnull => services.GetRequiredService<T>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Creates the bed. A given root folder is kept afterwards; otherwise a new temporary one is made and deleted.
    /// </summary>
    public static async Task<ImportTestBed> CreateAsync(int cpu = 2, string? root = null)
    {
        var ownsRoot = root is null;
        root ??= Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var library = await LibraryTestBed.CreateAsync();
        var folder = IndexedFolder.Create(library.Library.Library.Id, root, root, LibraryTestBed.Start);
        await library.Library.Folders.AddAsync(folder, Token);

        var settings = new ImportTestSettings { Current = new AppSettings { Processing = new ProcessingSettings { CpuConcurrency = cpu } } };
        var capabilities = new CapabilityService([], NullLogger<CapabilityService>.Instance);
        if (ThumbnailTestSupport.HasFfmpeg)
        {
            capabilities.SetAvailable(KnownCapabilities.VideoAnalysis);
        }

        var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<ICapabilityService>(capabilities)
            .AddSingleton<ISettingsService>(settings)
            .AddSingleton<IHardwareInfo>(new ImportTestHardware())
            .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>()
            .AddSingleton<IFolderDiscovery, FolderDiscovery>()
            .AddSingleton<IFileHasher, XxHashFileHasher>()
            .AddSingleton<IPerceptualHasher, SkiaPerceptualHasher>()
            .AddSingleton<IMediaHasher, MediaHasher>()
            .AddSingleton<ILibrarySession>(new BedSession(library))
            .AddSingleton<RegisteredRecorder>()
            .AddSingleton<INotificationHandler<MediaItemRegistered>>(provider => provider.GetRequiredService<RegisteredRecorder>())
            .AddJobs()
            .AddMedia()
            .AddUseCases()
            .BuildServiceProvider();
        var bed = new ImportTestBed(library, root, folder, services, settings) { ownsRoot = ownsRoot };
        await bed.Jobs.StartAsync(library.Library, Token);
        return bed;
    }

    public string Copy(string source, string relativePath)
    {
        var target = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Writes copies of a JPEG that differ in their trailing bytes, which decoders ignore, so each has its own hashes.
    /// </summary>
    public void WriteDistinctPhotos(int count, string directory = "photos")
    {
        var photo = File.ReadAllBytes(Testing.Media.SampleMedia.JpegWithExif);
        Directory.CreateDirectory(Path.Combine(Root, directory));
        for (var i = 0; i < count; i++)
        {
            File.WriteAllBytes(Path.Combine(Root, directory, $"IMG_{i:D5}.jpg"), [.. photo, .. BitConverter.GetBytes(i)]);
        }
    }

    public async Task<Id<ProcessingJob>> ImportAsync()
    {
        var started = await Sender.Send(new ImportFoldersCommand([Folder.Id]), Token);
        return Assert.Single(started);
    }

    public async Task WaitUntilIdleAsync(TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (await Library.Jobs.CountPendingAsync(Token) > 0)
        {
            Assert.True(stopwatch.Elapsed < (timeout ?? TimeSpan.FromSeconds(60)), "Timed out waiting for the import.");
            await Task.Delay(20, Token);
        }
    }

    public async Task<IReadOnlyList<MediaItem>> ItemsAsync() =>
        [.. (await Library.Items.GetPageAsync(0, 100_000, Token)).Where(item => item.FolderId == Folder.Id)];

    public async Task RestartAsync()
    {
        await Jobs.StopAsync();
        await LibraryBed.ReopenAsync();
        await Jobs.StartAsync(Library, Token);
    }

    public async ValueTask DisposeAsync()
    {
        await Jobs.StopAsync();
        await services.DisposeAsync();
        await LibraryBed.DisposeAsync();
        if (!ownsRoot)
        {
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be locked by a test; the temp folder is cleaned up eventually.
        }
    }
}

/// <summary>
/// The bed's library as the open one, also after it's reopened.
/// </summary>
internal sealed class BedSession(LibraryTestBed library) : ILibrarySession
{
    public event EventHandler? CurrentChanged
    {
        add { }
        remove { }
    }

    public IOpenLibrary? Current => library.Library;

    public Task SetCurrentAsync(IOpenLibrary? library) => throw new NotSupportedException();
}

/// <summary>
/// Records the <see cref="MediaItemRegistered"/> notifications.
/// </summary>
internal sealed class RegisteredRecorder : INotificationHandler<MediaItemRegistered>
{
    public ConcurrentQueue<MediaItemRegistered> Received { get; } = new();

    public Task Handle(MediaItemRegistered notification, CancellationToken cancellationToken)
    {
        Received.Enqueue(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Settings held in memory.
/// </summary>
internal sealed class ImportTestSettings : ISettingsService
{
    public AppSettings Current { get; set; } = new();

    public void Save(AppSettings settings) => Current = settings;

    public AppSettings Update(Func<AppSettings, AppSettings> change) => Current = change(Current);
}

/// <summary>
/// A fixed machine.
/// </summary>
internal sealed class ImportTestHardware : IHardwareInfo
{
    public HardwareProfile Profile { get; } = new("X64", 8, 8L << 30, []);

    public ExecutionProvider PreferredExecutionProvider => ExecutionProvider.Cpu;

    public int DefaultParallelism => 4;
}
