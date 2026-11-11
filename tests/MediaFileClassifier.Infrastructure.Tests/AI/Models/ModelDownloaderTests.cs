using System.Net;
using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Models;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Models;

/// <summary>
/// Tests for <see cref="ModelDownloader"/>: a good download is installed, and a damaged, truncated, failed or
/// cancelled one is rejected and leaves nothing behind, neither in the store nor in its <c>.downloads</c> folder.
/// </summary>
public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly ModelStore store;
    private readonly FakeHandler handler = new();
    private readonly byte[] vision = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17);
    private readonly byte[] text = RandomNumberGenerator.GetBytes(1024 * 1024);
    private readonly ModelCatalogEntry entry;
    private long? freeSpace;

    public ModelDownloaderTests()
    {
        store = new ModelStore(Path.Combine(root, "models"), TimeProvider.System);
        entry = new ModelCatalogEntry(
            "siglip2",
            "1",
            "SigLIP 2",
            ModelPurpose.Embedding,
            "Apache-2.0",
            new Uri("https://example.com/license"),
            new Uri("https://example.com/siglip2"),
            "standard",
            [new ModelTier("standard", "Standard", [File("vision", vision), File("text", text)])]);
        handler.Serve(entry.Default.Files[0].Url, vision);
        handler.Serve(entry.Default.Files[1].Url, text);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string DownloadsDirectory => Path.Combine(store.RootDirectory, ModelDownloader.DownloadsFolderName);

    public void Dispose()
    {
        store.Dispose();
        handler.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Download_GoodFiles_AreInstalledWithProgress()
    {
        var reports = new List<ModelDownloadProgress>();
        using var downloader = CreateDownloader();

        var model = await downloader.DownloadAsync(entry, "standard", new SyncProgress(reports.Add), Token);

        Assert.Equal(SHA256.HashData(vision), SHA256.HashData(System.IO.File.ReadAllBytes(model.GetFilePaths()["vision"])));
        Assert.Equal(text.Length, new FileInfo(model.GetFilePaths()["text"]).Length);
        Assert.Equal("Apache-2.0", model.License);
        Assert.Equal("https://example.com/siglip2", model.Source);
        Assert.Equivalent(model, await store.FindAsync("siglip2", "1", "standard", Token));
        Assert.Empty(await store.VerifyAsync("siglip2", "1", "standard", Token));
        Assert.Equal(new ModelDownloadProgress(ModelDownloadPhase.Downloading, entry.Default.Size, entry.Default.Size), reports.Last(report => report.Phase == ModelDownloadPhase.Downloading));
        Assert.Contains(reports, report => report.Phase == ModelDownloadPhase.Installing);
        AssertNoDownloadsLeft();
    }

    [Fact]
    public async Task Download_CorruptedFile_IsRejected()
    {
        var corrupted = (byte[])text.Clone();
        corrupted[^1] ^= 0xFF;
        handler.Serve(entry.Default.Files[1].Url, corrupted);
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.HashMismatch, error.Failure);
        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
        await AssertNothingLeftAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_TruncatedFile_IsRejected(bool sendsLength)
    {
        handler.Serve(entry.Default.Files[0].Url, vision[..(vision.Length / 2)], sendsLength);
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.HashMismatch, error.Failure);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_LongerFile_IsRejected()
    {
        handler.Serve(entry.Default.Files[1].Url, [.. text, 1, 2, 3], sendsLength: false);
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.HashMismatch, error.Failure);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_Cancelled_LeavesNothingBehind()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        handler.Serve(entry.Default.Files[1].Url, text, onRead: cancellation.Cancel);
        using var downloader = CreateDownloader();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(entry, "standard", null, cancellation.Token));

        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_ServerError_IsANetworkFailure()
    {
        handler.Fail(entry.Default.Files[1].Url, HttpStatusCode.NotFound);
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Network, error.Failure);
        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_ConnectionDrops_IsANetworkFailure()
    {
        handler.Serve(entry.Default.Files[0].Url, vision, onRead: () => throw new HttpIOException(HttpRequestError.ResponseEnded, "The connection was closed."));
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Network, error.Failure);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_Unreachable_IsANetworkFailure()
    {
        handler.Throw(entry.Default.Files[0].Url, new HttpRequestException("No such host is known."));
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Network, error.Failure);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_Stalls_IsANetworkFailure()
    {
        handler.Serve(entry.Default.Files[0].Url, vision, stall: true);
        using var downloader = CreateDownloader(TimeSpan.FromMilliseconds(200));

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Network, error.Failure);
        Assert.Contains("nothing arrived", error.Message, StringComparison.Ordinal);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_NotEnoughSpace_FailsBeforeDownloading()
    {
        freeSpace = entry.Default.Size;
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.DiskFull, error.Failure);
        Assert.Equal(0, handler.Requests);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_DiskFillsUp_IsADiskFullFailure()
    {
        handler.Serve(entry.Default.Files[1].Url, text, onRead: () => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));
        using var downloader = CreateDownloader();

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.DiskFull, error.Failure);
        await AssertNothingLeftAsync();
    }

    [Fact]
    public async Task Download_AlreadyInstalled_IsRejectedWithoutDownloading()
    {
        using var downloader = CreateDownloader();
        await downloader.DownloadAsync(entry, "standard", null, Token);
        var requests = handler.Requests;

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Install, error.Failure);
        Assert.Equal(requests, handler.Requests);
        Assert.NotNull(await store.FindAsync("siglip2", "1", "standard", Token));
    }

    [Fact]
    public async Task Download_DeletesAbandonedDownloadsButNotRecentOnes()
    {
        var abandoned = Path.Combine(DownloadsDirectory, "abandoned");
        var recent = Path.Combine(DownloadsDirectory, "recent");
        Directory.CreateDirectory(abandoned);
        Directory.CreateDirectory(recent);
        var oldFile = Path.Combine(abandoned, "vision.onnx");
        await System.IO.File.WriteAllBytesAsync(oldFile, [1, 2, 3], Token);
        System.IO.File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-1));
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-1));
        await System.IO.File.WriteAllBytesAsync(Path.Combine(recent, "vision.onnx"), [1, 2, 3], Token);
        using var downloader = CreateDownloader();

        await downloader.DownloadAsync(entry, "standard", null, Token);

        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public async Task Download_StoreCantBeWritten_IsAnInstallFailure()
    {
        using var failing = new FailingImportStore(store, new UnauthorizedAccessException("Access to the path is denied."));
        using var downloader = new ModelDownloader(failing, handler, TimeSpan.FromSeconds(30), _ => null);

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => downloader.DownloadAsync(entry, "standard", null, Token));

        Assert.Equal(ModelDownloadFailure.Install, error.Failure);
        await AssertNothingLeftAsync();
    }

    private static ModelCatalogFile File(string role, byte[] content) =>
        new(role, $"{role}.onnx", new Uri($"https://example.com/{role}.onnx"), content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));

    private ModelDownloader CreateDownloader(TimeSpan? stallTimeout = null) =>
        new(store, handler, stallTimeout ?? TimeSpan.FromSeconds(30), _ => freeSpace);

    private async Task AssertNothingLeftAsync()
    {
        Assert.Null(await store.FindAsync("siglip2", "1", "standard", Token));
        Assert.Empty(await store.GetInstalledAsync(Token));
        AssertNoDownloadsLeft();
    }

    private void AssertNoDownloadsLeft() =>
        Assert.True(!Directory.Exists(DownloadsDirectory) || !Directory.EnumerateFileSystemEntries(DownloadsDirectory).Any(), "A download left files behind.");

    /// <summary>
    /// A model store whose import fails, e.g. because its folder can't be written.
    /// </summary>
    private sealed class FailingImportStore(ModelStore inner, Exception failure) : IModelStore, IDisposable
    {
        public event EventHandler? ModelsChanged
        {
            add => inner.ModelsChanged += value;
            remove => inner.ModelsChanged -= value;
        }

        public string RootDirectory => inner.RootDirectory;

        public Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken) => inner.GetInstalledAsync(cancellationToken);

        public Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => inner.FindAsync(modelId, version, tierId, cancellationToken);

        public Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken) => inner.FindTiersAsync(modelId, version, cancellationToken);

        public Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken) => throw failure;

        public Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) => inner.VerifyAsync(modelId, version, tierId, cancellationToken);

        public Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken) =>
            inner.RecordUsageAsync(modelId, version, contentHash, libraryPath, cancellationToken);

        public Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken) =>
            inner.GetUsingLibrariesAsync(modelId, version, tierId, cancellationToken);

        public Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken) =>
            inner.DeleteAsync(modelId, version, tierId, confirmed, cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class SyncProgress(Action<ModelDownloadProgress> report) : IProgress<ModelDownloadProgress>
    {
        public void Report(ModelDownloadProgress value) => report(value);
    }

    /// <summary>
    /// Serves fixed responses by URL.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<Uri, Func<HttpResponseMessage>> responses = [];
        private int requests;

        public int Requests => requests;

        public void Serve(Uri url, byte[] content, bool sendsLength = true, Action? onRead = null, bool stall = false) =>
            responses[url] = () =>
            {
                var body = new StreamContent(new ServedStream(content, onRead, stall));
                if (sendsLength)
                {
                    body.Headers.ContentLength = content.Length;
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = body };
            };

        public void Fail(Uri url, HttpStatusCode status) => responses[url] = () => new HttpResponseMessage(status);

        public void Throw(Uri url, Exception exception) => responses[url] = () => throw exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(responses[request.RequestUri!]());
        }
    }

    /// <summary>
    /// A response body that can run an action on its second read (after some bytes arrived) or stop sending.
    /// </summary>
    private sealed class ServedStream(byte[] content, Action? onRead, bool stall) : MemoryStream(content, writable: false)
    {
        private int reads;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++reads == 2)
            {
                onRead?.Invoke();
                if (stall)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 64 * 1024)], cancellationToken);
        }
    }
}
