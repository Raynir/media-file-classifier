using System.Net;
using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.AI.Models;

/// <summary>
/// Downloads catalogue models over HTTPS into the model store's <c>.downloads</c> folder, checks each file's size and
/// SHA-256 as it arrives, then has the store move them in with <see cref="ModelImportRequest.MoveSourceFiles"/>
/// (E06-S12-T02). Downloads run one at a time; resuming is Later (E19-S03).
/// </summary>
/// <remarks>
/// The temporary files sit on the store's own volume, so the move into the store is a rename, and the free-space check
/// looks at the right drive. Each download gets its own folder, which is deleted however the download ends. Folders
/// left by a crash are deleted by the next download.
/// </remarks>
public sealed partial class ModelDownloader : IModelDownloader, IDisposable
{
    /// <summary>
    /// Name of the store's subfolder that holds downloads in progress.
    /// </summary>
    internal const string DownloadsFolderName = ".downloads";

    private const int BufferSize = 1 << 20;

    // Windows ERROR_HANDLE_DISK_FULL and ERROR_DISK_FULL, as HResults; ENOSPC elsewhere.
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int DiskFull = unchecked((int)0x80070070);
    private const int NoSpace = 28;

    /// <summary>
    /// Room kept free beyond the model's size, so the download doesn't fill the disk to the last byte.
    /// </summary>
    private const long FreeSpaceMargin = 100L * 1024 * 1024;

    /// <summary>
    /// A folder in <c>.downloads</c> whose files haven't changed for this long belongs to no running download.
    /// </summary>
    private static readonly TimeSpan AbandonedAge = TimeSpan.FromMinutes(10);

    private readonly IModelStore store;
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly TimeSpan stallTimeout;
    private readonly Func<string, long?> getFreeSpace;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloader"/> class.
    /// </summary>
    /// <param name="store">The model store.</param>
    /// <param name="logger">Logger.</param>
    public ModelDownloader(IModelStore store, ILogger<ModelDownloader>? logger = null)
        : this(store, new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None }, TimeSpan.FromMinutes(1), GetFreeSpace, logger)
    {
        ownsClient = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelDownloader"/> class with a given HTTP handler.
    /// </summary>
    /// <param name="store">The model store.</param>
    /// <param name="handler">The HTTP handler.</param>
    /// <param name="stallTimeout">How long a download may receive nothing before it fails.</param>
    /// <param name="getFreeSpace">Free bytes on the drive of a folder, or <see langword="null"/> if unknown.</param>
    /// <param name="logger">Logger.</param>
    internal ModelDownloader(IModelStore store, HttpMessageHandler handler, TimeSpan stallTimeout, Func<string, long?> getFreeSpace, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(getFreeSpace);

        this.store = store;

        // The whole download can take long on a slow line; a stalled one is caught by the stall timeout instead.
        httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFileClassifier");
        this.stallTimeout = stallTimeout;
        this.getFreeSpace = getFreeSpace;
        this.logger = logger ?? NullLogger.Instance;
    }

    private string DownloadsDirectory => Path.Combine(store.RootDirectory, DownloadsFolderName);

    /// <inheritdoc />
    public async Task<InstalledModel> DownloadAsync(
        ModelCatalogEntry entry,
        string tierId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var tier = entry.GetTier(tierId);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var folder = Path.Combine(DownloadsDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            if (await store.FindAsync(entry.ModelId, entry.Version, cancellationToken).ConfigureAwait(false) is not null)
            {
                throw new ModelDownloadException(ModelDownloadFailure.Install, $"{entry.DisplayName} is already installed.");
            }

            DeleteAbandonedDownloads();
            EnsureFreeSpace(entry, tier);
            Directory.CreateDirectory(folder);
            LogDownloadStarted(entry.ModelId, entry.Version, tier.TierId, tier.Size);

            var sources = new List<ModelFileSource>();
            long done = 0;
            foreach (var file in tier.Files)
            {
                var path = Path.Combine(folder, file.FileName);
                var offset = done;
                await DownloadFileAsync(file, path, bytes => progress?.Report(new(ModelDownloadPhase.Downloading, offset + bytes, tier.Size)), cancellationToken)
                    .ConfigureAwait(false);
                done += file.Size;
                sources.Add(new ModelFileSource(file.Role, path, file.Sha256, file.FileName));
            }

            var request = new ModelImportRequest(entry.ModelId, entry.Version, entry.DisplayName, sources)
            {
                License = entry.License,
                Source = entry.Source.ToString(),
                MoveSourceFiles = true,
            };
            var installing = progress is null ? null : new InstallProgress(progress, tier.Size);
            try
            {
                var model = await store.ImportAsync(request, installing, cancellationToken).ConfigureAwait(false);
                LogDownloadFinished(entry.ModelId, entry.Version);
                return model;
            }
            catch (ModelStoreException ex)
            {
                // The files were checked as they arrived, so this is e.g. a parallel import of the same model.
                throw new ModelDownloadException(ModelDownloadFailure.Install, $"{entry.DisplayName} couldn't be installed: {ex.Message}", ex);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogDownloadCancelled(entry.ModelId, entry.Version);
            throw;
        }
        catch (ModelDownloadException ex)
        {
            LogDownloadFailed(ex, entry.ModelId, entry.Version, ex.Failure);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // E.g. the store's folder can't be written, or a file is locked while it's moved in.
            var failure = ex is IOException io && IsDiskFull(io)
                ? DiskFullException(entry, ex)
                : new ModelDownloadException(ModelDownloadFailure.Install, $"{entry.DisplayName} couldn't be saved in {store.RootDirectory}: {ex.Message}", ex);
            LogDownloadFailed(ex, entry.ModelId, entry.Version, failure.Failure);
            throw failure;
        }
        finally
        {
            TryDeleteDirectory(folder);
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }

        gate.Dispose();
    }

    private static long? GetFreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsDiskFull(IOException exception) =>
        OperatingSystem.IsWindows() ? exception.HResult is HandleDiskFull or DiskFull : exception.HResult == NoSpace;

    private static ModelDownloadException DiskFullException(ModelCatalogEntry entry, Exception? inner = null) =>
        new(ModelDownloadFailure.DiskFull, $"There isn't enough free disk space for {entry.DisplayName}.", inner);

    private static ModelDownloadException NetworkException(ModelCatalogFile file, string problem, Exception? inner = null) =>
        new(ModelDownloadFailure.Network, $"{file.FileName} couldn't be downloaded from {file.Url.Host}: {problem}", inner);

    private static ModelDownloadException MismatchException(ModelCatalogFile file, string problem) =>
        new(ModelDownloadFailure.HashMismatch, $"{file.FileName} arrived damaged or isn't the expected file: {problem}");

    private async Task DownloadFileAsync(ModelCatalogFile file, string path, Action<long> onProgress, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var saving = false;
        stall.CancelAfter(stallTimeout);
        try
        {
            using var response = await httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw NetworkException(file, $"the server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Content.Headers.ContentLength is { } length && length != file.Size)
            {
                throw MismatchException(file, $"the server sends {length:N0} bytes, but {file.Size:N0} were expected.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long received = 0;
            var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                saving = true;
                var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                await using (target.ConfigureAwait(false))
                {
                    int read;
                    saving = false;
                    while ((read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
                    {
                        stall.CancelAfter(stallTimeout);
                        received += read;
                        if (received > file.Size)
                        {
                            throw MismatchException(file, $"more than the expected {file.Size:N0} bytes arrived.");
                        }

                        hash.AppendData(buffer, 0, read);
                        saving = true;
                        await target.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                        saving = false;
                        onProgress(received);
                    }
                }
            }

            if (received != file.Size)
            {
                throw MismatchException(file, $"only {received:N0} of {file.Size:N0} bytes arrived.");
            }

            var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw MismatchException(file, $"its SHA-256 is {sha256}, but {file.Sha256.ToLowerInvariant()} was expected.");
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw NetworkException(file, $"nothing arrived for {stallTimeout.TotalSeconds:N0} seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw NetworkException(file, ex.Message, ex);
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new ModelDownloadException(ModelDownloadFailure.DiskFull, $"There isn't enough free disk space for {file.FileName}.", ex);
        }
        catch (IOException ex) when (!saving)
        {
            throw NetworkException(file, ex.Message, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ModelDownloadException(ModelDownloadFailure.Install, $"{file.FileName} couldn't be saved to {path}: {ex.Message}", ex);
        }
    }

    private void EnsureFreeSpace(ModelCatalogEntry entry, ModelTier tier)
    {
        Directory.CreateDirectory(store.RootDirectory);
        if (getFreeSpace(store.RootDirectory) is { } free && free < tier.Size + FreeSpaceMargin)
        {
            throw new ModelDownloadException(
                ModelDownloadFailure.DiskFull,
                $"{entry.DisplayName} needs {FormatSize(tier.Size + FreeSpaceMargin)} of free space, but the drive has {FormatSize(free)}.");
        }
    }

    private void DeleteAbandonedDownloads()
    {
        if (!Directory.Exists(DownloadsDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - AbandonedAge;
        foreach (var directory in Directory.EnumerateDirectories(DownloadsDirectory))
        {
            try
            {
                var lastWrite = Directory.EnumerateFiles(directory)
                    .Select(File.GetLastWriteTimeUtc)
                    .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory))
                    .Max();
                if (lastWrite < cutoff)
                {
                    TryDeleteDirectory(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogCleanupFailed(ex, directory);
            }
        }
    }

    private static string FormatSize(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next download deletes it once it's abandoned.
            LogCleanupFailed(ex, directory);
        }
    }

    /// <summary>
    /// Passes the store's byte count on as install progress, in order, on the reporting thread.
    /// </summary>
    private sealed class InstallProgress(IProgress<ModelDownloadProgress> progress, long total) : IProgress<long>
    {
        public void Report(long value) => progress.Report(new(ModelDownloadPhase.Installing, value, total));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading model {ModelId} {Version}, tier {TierId} ({Size} bytes)")]
    private partial void LogDownloadStarted(string modelId, string version, string tierId, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded model {ModelId} {Version}")]
    private partial void LogDownloadFinished(string modelId, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Download of model {ModelId} {Version} cancelled")]
    private partial void LogDownloadCancelled(string modelId, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download of model {ModelId} {Version} failed ({Failure})")]
    private partial void LogDownloadFailed(Exception exception, string modelId, string version, ModelDownloadFailure failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete the download folder {Directory}")]
    private partial void LogCleanupFailed(Exception exception, string directory);
}
