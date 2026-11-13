using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Testing.AI;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Models;

/// <summary>
/// Tests for the tiers in <see cref="ModelStore"/> (E06-S08-T03): tiers side by side, usage per tier, and the move of
/// folders from before tiers into their tier folder.
/// </summary>
public sealed class ModelStoreTierTests : IDisposable
{
    private const string Standard = "standard";
    private const string Compact = "compact";

    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly string sources;
    private readonly string models;
    private readonly (string Path, string Sha256) vision;
    private readonly (string Path, string Sha256) text;
    private readonly (string Path, string Sha256) textFp16;
    private readonly SingleEntryModelCatalog catalog;
    private readonly ModelStore store;

    public ModelStoreTierTests()
    {
        sources = Path.Combine(root, "sources");
        models = Path.Combine(root, "models");
        Directory.CreateDirectory(sources);
        vision = WriteSource("vision_model.onnx", 300);
        text = WriteSource("text_model.onnx", 200);
        textFp16 = WriteSource("text_model_fp16.onnx", 100);
        catalog = new SingleEntryModelCatalog(Entry());
        store = new ModelStore(models, TimeProvider.System, catalog: catalog);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_TwoTiers_AreInstalledSideBySideAndDeletedApart()
    {
        var standard = await store.ImportAsync(Request(Standard), null, Token);
        var compact = await store.ImportAsync(Request(Compact), null, Token);

        Assert.Equal([Compact, Standard], (await store.FindTiersAsync("siglip2", "1", Token)).Select(model => model.TierId));
        Assert.Equal(Path.Combine(models, "siglip2", "1", Compact), compact.Directory);
        Assert.NotEqual(standard.ContentHash, compact.ContentHash);
        Assert.Equal(2, (await store.GetInstalledAsync(Token)).Count);

        Assert.True((await store.DeleteAsync("siglip2", "1", Compact, confirmed: false, Token)).Deleted);

        Assert.Equivalent(standard, Assert.Single(await store.FindTiersAsync("siglip2", "1", Token)));
        Assert.Null(await store.FindAsync("siglip2", "1", Compact, Token));
        Assert.Empty(await store.VerifyAsync("siglip2", "1", Standard, Token));
    }

    [Fact]
    public async Task Import_SameTierTwice_Fails()
    {
        await store.ImportAsync(Request(Standard), null, Token);

        await Assert.ThrowsAsync<ModelStoreException>(() => store.ImportAsync(Request(Standard), null, Token));
    }

    [Fact]
    public async Task Usage_IsRecordedPerTierByContentHash()
    {
        await store.ImportAsync(Request(Standard), null, Token);
        var compact = await store.ImportAsync(Request(Compact), null, Token);
        var library = Path.Combine(root, "Holidays.mfclib");

        await store.RecordUsageAsync("siglip2", "1", compact.ContentHash, library, Token);

        Assert.Equal([library], await store.GetUsingLibrariesAsync("siglip2", "1", Compact, Token));
        Assert.Empty(await store.GetUsingLibrariesAsync("siglip2", "1", Standard, Token));
        Assert.True((await store.DeleteAsync("siglip2", "1", Standard, confirmed: false, Token)).Deleted);
        Assert.True((await store.DeleteAsync("siglip2", "1", Compact, confirmed: false, Token)).NeedsConfirmation);
        await Assert.ThrowsAsync<ModelStoreException>(() => store.RecordUsageAsync("siglip2", "1", new string('0', 64), library, Token));
    }

    [Theory]
    [InlineData(Standard)]
    [InlineData(Compact)]
    public async Task Legacy_FolderIsMovedIntoTheTierWhoseFilesItHas(string tierId)
    {
        var installed = await MakeLegacyAsync(tierId);
        var library = Path.Combine(root, "Holidays.mfclib");
        await File.WriteAllTextAsync(Path.Combine(models, "usage.json"), JsonSerializer.Serialize(new Dictionary<string, string[]> { ["siglip2/1"] = [library] }), Token);
        using var reopened = new ModelStore(models, TimeProvider.System, catalog: catalog);

        var model = Assert.Single(await reopened.GetInstalledAsync(Token));

        Assert.Equal(tierId, model.TierId);
        Assert.Equal(installed.ContentHash, model.ContentHash);
        Assert.Equal(Path.Combine(models, "siglip2", "1", tierId), model.Directory);
        Assert.False(File.Exists(Path.Combine(models, "siglip2", "1", "manifest.json")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(models, "siglip2"), ".migrating-*"));
        Assert.Equal(tierId, JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(model.Directory, "manifest.json"), Token))!["tierId"]!.GetValue<string>());
        Assert.Empty(await reopened.VerifyAsync("siglip2", "1", tierId, Token));
        Assert.Equal([library], await reopened.GetUsingLibrariesAsync("siglip2", "1", tierId, Token));
        Assert.Contains($"siglip2/1/{tierId}", await File.ReadAllTextAsync(Path.Combine(models, "usage.json"), Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legacy_WithoutACatalogue_IsTheStandardTier()
    {
        await MakeLegacyAsync(Compact);
        using var reopened = new ModelStore(models, TimeProvider.System);

        Assert.Equal(Standard, Assert.Single(await reopened.FindTiersAsync("siglip2", "1", Token)).TierId);
    }

    [Fact]
    public async Task Legacy_MoveInterruptedBetweenRenames_IsFinishedOnNextAccess()
    {
        await MakeLegacyAsync(Compact);
        Directory.Move(Path.Combine(models, "siglip2", "1"), Path.Combine(models, "siglip2", ".migrating-1"));
        using var reopened = new ModelStore(models, TimeProvider.System, catalog: catalog);

        var model = await reopened.FindAsync("siglip2", "1", Compact, Token);

        Assert.NotNull(model);
        Assert.Equal(Path.Combine(models, "siglip2", "1", Compact), model.Directory);
        Assert.False(Directory.Exists(Path.Combine(models, "siglip2", ".migrating-1")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_MoveThatCanNeverFinish_IsSetAside_AndDoesNotBlockImportOrDelete(bool targetTaken)
    {
        await MakeLegacyAsync(Compact);
        var modelFolder = Path.Combine(models, "siglip2");
        var pending = Path.Combine(modelFolder, ".migrating-1");
        Directory.Move(Path.Combine(modelFolder, "1"), pending);
        if (targetTaken)
        {
            Directory.CreateDirectory(Path.Combine(modelFolder, "1", Compact));
        }
        else
        {
            File.WriteAllText(Path.Combine(pending, "manifest.json"), "{ not json");
        }

        using var reopened = new ModelStore(models, TimeProvider.System, catalog: catalog);

        _ = await reopened.FindTiersAsync("siglip2", "1", Token);

        Assert.False(Directory.Exists(pending));
        Assert.Single(Directory.EnumerateDirectories(modelFolder, ".unmigrated-1-*"));
        await reopened.ImportAsync(Request(Standard), null, Token);
        Assert.Contains(await reopened.FindTiersAsync("siglip2", "1", Token), tier => tier.TierId == Standard);
    }

    [Fact]
    public async Task Legacy_FileOpen_IsReadInPlaceAndMovedLater()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to move a folder with an open file.");
        await MakeLegacyAsync(Standard);
        var legacyDirectory = Path.Combine(models, "siglip2", "1");
        using var reopened = new ModelStore(models, TimeProvider.System, catalog: catalog);

        await using (File.OpenRead(Path.Combine(legacyDirectory, "vision_model.onnx")))
        {
            var model = Assert.Single(await reopened.FindTiersAsync("siglip2", "1", Token));
            Assert.Equal(Standard, model.TierId);
            Assert.Equal(legacyDirectory, model.Directory);
            await Assert.ThrowsAsync<ModelStoreException>(() => reopened.ImportAsync(Request(Compact), null, Token));
        }

        Assert.Equal(Path.Combine(legacyDirectory, Standard), Assert.Single(await reopened.FindTiersAsync("siglip2", "1", Token)).Directory);
    }

    public void Dispose()
    {
        store.Dispose();
        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Installs a tier, then puts it back in the layout from before tiers: files and manifest (without a tier) in the
    /// version folder.
    /// </summary>
    private async Task<InstalledModel> MakeLegacyAsync(string tierId)
    {
        var model = await store.ImportAsync(Request(tierId), null, Token);
        var versionDirectory = Path.GetDirectoryName(model.Directory)!;
        foreach (var file in Directory.EnumerateFiles(model.Directory))
        {
            File.Move(file, Path.Combine(versionDirectory, Path.GetFileName(file)));
        }

        Directory.Delete(model.Directory);
        var manifestPath = Path.Combine(versionDirectory, "manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, Token))!.AsObject();
        manifest.Remove("tierId");
        manifest["format"] = 1;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), Token);
        return model;
    }

    private ModelImportRequest Request(string tierId)
    {
        var tier = Entry().GetTier(tierId);
        var paths = new Dictionary<string, string> { [vision.Sha256] = vision.Path, [text.Sha256] = text.Path, [textFp16.Sha256] = textFp16.Path };
        return new("siglip2", "1", tierId, "SigLIP 2", tier.Files.Select(file => new ModelFileSource(file.Role, paths[file.Sha256], file.Sha256, file.FileName)).ToList());
    }

    private ModelCatalogEntry Entry()
    {
        var url = new Uri("https://example.com/model");
        ModelCatalogFile File((string Path, string Sha256) source, string role) => new(role, Path.GetFileName(source.Path), url, new FileInfo(source.Path).Length, source.Sha256);
        return new ModelCatalogEntry(
            "siglip2",
            "1",
            "SigLIP 2",
            ModelPurpose.Embedding,
            "Apache-2.0",
            url,
            url,
            Standard,
            [
                new ModelTier(Standard, "Standard", [File(vision, "vision"), File(text, "text")]),
                new ModelTier(Compact, "Compact", [File(vision, "vision"), File(textFp16, "text")]) { Precision = "fp16-text", PreferredExecutionProvider = ExecutionProvider.DirectMl },
            ]);
    }

    private (string Path, string Sha256) WriteSource(string name, int size)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        var path = Path.Combine(sources, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return (path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
