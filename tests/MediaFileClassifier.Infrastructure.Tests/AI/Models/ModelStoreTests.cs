using System.Security.Cryptography;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Models;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Models;

/// <summary>
/// Tests for <see cref="ModelStore"/>: importing with hash checks, verifying, usage records and deletion.
/// </summary>
public sealed class ModelStoreTests : IDisposable
{
    private const string Standard = "standard";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly string sources;
    private readonly ModelStore store;

    public ModelStoreTests()
    {
        sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(sources);
        store = new ModelStore(Path.Combine(root, "models"), new FixedTimeProvider(Now));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_LocalFileWithMatchingHash_IsInstalledAndCanBeDeleted()
    {
        var (path, sha256) = WriteSource("vision.onnx", 3_000_000);

        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path, sha256.ToUpperInvariant())), null, Token);

        Assert.Equal(sha256, Assert.Single(model.Files).Sha256);
        Assert.Equal(3_000_000, model.Files[0].Size);
        Assert.True(File.Exists(model.GetFilePaths()["vision"]));
        Assert.True(File.Exists(path), "Importing copies by default.");
        Assert.Equivalent(model, await store.FindAsync("siglip2", "1", Standard, Token));
        Assert.Empty(await store.VerifyAsync("siglip2", "1", Standard, Token));

        var deletion = await store.DeleteAsync("siglip2", "1", Standard, confirmed: false, Token);

        Assert.True(deletion.Deleted);
        Assert.Null(await store.FindAsync("siglip2", "1", Standard, Token));
        Assert.Empty(await store.GetInstalledAsync(Token));
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "siglip2")));
        Assert.Equal(Path.Combine(store.RootDirectory, "siglip2", "1", Standard), model.Directory);
    }

    [Fact]
    public async Task Import_HashMismatch_FailsAndLeavesNothing()
    {
        var (path, _) = WriteSource("vision.onnx", 1000);

        var exception = await Assert.ThrowsAsync<ModelStoreException>(
            () => store.ImportAsync(Request(new ModelFileSource("vision", path, new string('0', 64))), null, Token));

        Assert.Contains("vision.onnx", exception.Message, StringComparison.Ordinal);
        Assert.Null(await store.FindAsync("siglip2", "1", Standard, Token));
        Assert.Empty(Directory.EnumerateFiles(store.RootDirectory, "*.onnx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Import_SeveralFiles_ReportsProgressAndContentHash()
    {
        var (vision, visionHash) = WriteSource("vision.onnx", 2_500_000);
        var (text, textHash) = WriteSource("text.onnx", 1_500_000);
        var reports = new List<long>();

        var model = await store.ImportAsync(
            Request(new ModelFileSource("vision", vision, visionHash), new ModelFileSource("text", text, textHash, "text_model.onnx")),
            new SynchronousProgress(reports.Add),
            Token);

        Assert.Equal(4_000_000, reports[^1]);
        Assert.Equal(reports.Order(), reports);
        Assert.Equal(["vision.onnx", "text_model.onnx"], model.Files.Select(file => file.FileName));
        Assert.Equal(64, model.ContentHash.Length);
        Assert.Equal(ModelStore.ComputeContentHash(model.Files.Reverse()), model.ContentHash);
    }

    [Fact]
    public async Task Import_MoveSourceFiles_MovesThem()
    {
        var (path, _) = WriteSource("vision.onnx", 1000);

        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path)) with { MoveSourceFiles = true }, null, Token);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(model.GetFilePaths()["vision"]));
    }

    [Fact]
    public async Task Import_MoveSourceFilesWithWrongHash_KeepsTheSourceFile()
    {
        var (path, _) = WriteSource("vision.onnx", 1000);

        await Assert.ThrowsAsync<ModelStoreException>(
            () => store.ImportAsync(Request(new ModelFileSource("vision", path, new string('0', 64))) with { MoveSourceFiles = true }, null, Token));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Import_SameVersionTwice_Fails()
    {
        var (path, _) = WriteSource("vision.onnx", 100);
        await store.ImportAsync(Request(new ModelFileSource("vision", path)), null, Token);

        await Assert.ThrowsAsync<ModelStoreException>(() => store.ImportAsync(Request(new ModelFileSource("vision", path)), null, Token));
    }

    [Fact]
    public async Task Import_MissingSource_Fails()
    {
        await Assert.ThrowsAsync<ModelStoreException>(
            () => store.ImportAsync(Request(new ModelFileSource("vision", Path.Combine(sources, "missing.onnx"))), null, Token));
    }

    [Theory]
    [InlineData("..", "1", Standard)]
    [InlineData("siglip2", "1", "..")]
    [InlineData("siglip2", "1", "Compact")]
    [InlineData("../evil", "1", Standard)]
    [InlineData("siglip2", "..\\..", Standard)]
    [InlineData("SigLIP", "1", Standard)]
    [InlineData(".hidden", "1", Standard)]
    [InlineData("a/b", "1", Standard)]
    public async Task Import_UnsafeIdVersionOrTier_IsRejected(string modelId, string version, string tierId)
    {
        var (path, _) = WriteSource("vision.onnx", 100);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ImportAsync(new ModelImportRequest(modelId, version, tierId, "Model", [new ModelFileSource("vision", path)]), null, Token));
    }

    [Theory]
    [InlineData("..\\escape.onnx")]
    [InlineData("sub/file.onnx")]
    [InlineData("manifest.json")]
    [InlineData(".hidden")]
    public async Task Import_UnsafeFileName_IsRejected(string fileName)
    {
        var (path, _) = WriteSource("vision.onnx", 100);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ImportAsync(Request(new ModelFileSource("vision", path, FileName: fileName)), null, Token));
    }

    [Fact]
    public async Task Import_DuplicateRole_IsRejected()
    {
        var (a, _) = WriteSource("a.onnx", 10);
        var (b, _) = WriteSource("b.onnx", 10);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ImportAsync(Request(new ModelFileSource("vision", a), new ModelFileSource("vision", b)), null, Token));
    }

    [Fact]
    public async Task Verify_ChangedOrMissingFile_IsReported()
    {
        var (a, _) = WriteSource("a.onnx", 100);
        var (b, _) = WriteSource("b.onnx", 100);
        var model = await store.ImportAsync(Request(new ModelFileSource("vision", a), new ModelFileSource("text", b)), null, Token);
        await File.WriteAllBytesAsync(model.GetFilePaths()["vision"], new byte[100], Token);
        File.Delete(model.GetFilePaths()["text"]);

        var problems = await store.VerifyAsync("siglip2", "1", Standard, Token);

        Assert.Equal(["a.onnx is damaged: its contents changed since it was installed.", "b.onnx is missing."], problems);
    }

    [Fact]
    public async Task Delete_ModelUsedByLibraries_NamesThemAndWaitsForConfirmation()
    {
        var (path, _) = WriteSource("vision.onnx", 100);
        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path)), null, Token);
        var holidays = Path.Combine(root, "Holidays.mfclib");
        await store.RecordUsageAsync("siglip2", "1", model.ContentHash, holidays, Token);
        await store.RecordUsageAsync("siglip2", "1", model.ContentHash.ToUpperInvariant(), holidays + Path.DirectorySeparatorChar, Token);
        await store.RecordUsageAsync("siglip2", "1", model.ContentHash, Path.Combine(root, "Work.mfclib"), Token);

        var unconfirmed = await store.DeleteAsync("siglip2", "1", Standard, confirmed: false, Token);

        Assert.False(unconfirmed.Deleted);
        Assert.True(unconfirmed.NeedsConfirmation);
        Assert.Equal([holidays, Path.Combine(root, "Work.mfclib")], unconfirmed.UsingLibraries);
        Assert.NotNull(await store.FindAsync("siglip2", "1", Standard, Token));

        var confirmed = await store.DeleteAsync("siglip2", "1", Standard, confirmed: true, Token);

        Assert.True(confirmed.Deleted);
        Assert.Equal(2, confirmed.UsingLibraries.Count);
        Assert.Null(await store.FindAsync("siglip2", "1", Standard, Token));
        Assert.Empty(await store.GetUsingLibrariesAsync("siglip2", "1", Standard, Token));
    }

    [Fact]
    public async Task Delete_NotInstalled_Fails()
    {
        await Assert.ThrowsAsync<ModelStoreException>(() => store.DeleteAsync("siglip2", "1", Standard, confirmed: true, Token));
    }

    [Fact]
    public async Task Delete_FileOpen_FailsAndKeepsTheModel()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to move a folder with an open file.");
        var (path, _) = WriteSource("vision.onnx", 100);
        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path)), null, Token);

        await using (File.OpenRead(model.GetFilePaths()["vision"]))
        {
            var exception = await Assert.ThrowsAsync<ModelStoreException>(() => store.DeleteAsync("siglip2", "1", Standard, confirmed: true, Token));
            Assert.Contains("in use", exception.Message, StringComparison.Ordinal);
        }

        Assert.Empty(await store.VerifyAsync("siglip2", "1", Standard, Token));
    }

    [Fact]
    public async Task GetInstalled_SkipsFoldersWithoutAValidManifest()
    {
        var (path, _) = WriteSource("vision.onnx", 100);
        await store.ImportAsync(Request(new ModelFileSource("vision", path)), null, Token);
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "broken", "1"));
        await File.WriteAllTextAsync(Path.Combine(store.RootDirectory, "broken", "1", "manifest.json"), "{ not json", Token);
        Directory.CreateDirectory(Path.Combine(store.RootDirectory, "empty", "1"));

        var installed = await store.GetInstalledAsync(Token);

        Assert.Equal("siglip2", Assert.Single(installed).ModelId);
    }

    [Fact]
    public async Task Manifest_SurvivesANewStoreInstance()
    {
        var (path, sha256) = WriteSource("vision.onnx", 100);
        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path, sha256)) with { License = "Apache-2.0" }, null, Token);
        using var reopened = new ModelStore(store.RootDirectory, TimeProvider.System);

        var found = await reopened.FindAsync("siglip2", "1", Standard, Token);

        Assert.Equivalent(model, found);
        Assert.Equal("Apache-2.0", found!.License);
        Assert.Equal(Now, found.InstalledAt);
    }

    public void Dispose()
    {
        store.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ModelsChanged_IsRaisedAfterImportAndDeleteOnly()
    {
        var raised = 0;
        store.ModelsChanged += (_, _) => raised++;
        var (path, sha256) = WriteSource("vision.onnx", 1000);
        await Assert.ThrowsAsync<ModelStoreException>(() => store.ImportAsync(Request(new ModelFileSource("vision", path, new string('0', 64))), null, Token));
        Assert.Equal(0, raised);

        var model = await store.ImportAsync(Request(new ModelFileSource("vision", path, sha256)), null, Token);
        Assert.Equal(1, raised);

        await store.RecordUsageAsync("siglip2", "1", model.ContentHash, Path.Combine(root, "Holidays.mfclib"), Token);
        await store.DeleteAsync("siglip2", "1", Standard, confirmed: false, Token);
        Assert.Equal(1, raised);

        await store.DeleteAsync("siglip2", "1", Standard, confirmed: true, Token);
        Assert.Equal(2, raised);
    }

    private static ModelImportRequest Request(params ModelFileSource[] files) => new("siglip2", "1", Standard, "SigLIP 2", files);

    private (string Path, string Sha256) WriteSource(string name, int size)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        var path = Path.Combine(sources, name);
        File.WriteAllBytes(path, bytes);
        return (path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
