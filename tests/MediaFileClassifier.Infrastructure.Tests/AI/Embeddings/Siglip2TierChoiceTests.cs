using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>
/// Tests for the SigLIP 2 tier choice (E06-S08-T03): the CPU tier on a CPU-only machine, the GPU tier on a GPU machine,
/// with the shipped catalogue.
/// </summary>
public sealed class Siglip2TierChoiceTests : IDisposable
{
    private const string Standard = "standard";
    private const string Compact = "compact";

    private readonly string root = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly ModelCatalog catalog = new();
    private readonly ModelStore store;

    public Siglip2TierChoiceTests()
    {
        Directory.CreateDirectory(root);
        store = new ModelStore(Path.Combine(root, "models"), TimeProvider.System, catalog: catalog);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ExecutionProvider.Cpu, Standard)]
    [InlineData(ExecutionProvider.DirectMl, Compact)]
    [InlineData(ExecutionProvider.Cuda, Compact)]
    public void ChooseTier_BothInstalled_TakesTheOneThatSuitsTheProvider(ExecutionProvider provider, string expected)
    {
        var installed = new[] { Installed(Compact), Installed(Standard) };

        Assert.Equal(expected, Siglip2EmbeddingProvider.ChooseTier(installed, catalog, provider, null)?.TierId);
    }

    [Theory]
    [InlineData(ExecutionProvider.Cpu, Compact)]
    [InlineData(ExecutionProvider.DirectMl, Standard)]
    public void ChooseTier_OnlyTheOtherTierInstalled_TakesIt(ExecutionProvider provider, string only)
    {
        Assert.Equal(only, Siglip2EmbeddingProvider.ChooseTier([Installed(only)], catalog, provider, null)?.TierId);
    }

    [Theory]
    [InlineData(Compact, Compact)]
    [InlineData("missing", Standard)]
    public void ChooseTier_ConfiguredTier_WinsWhenInstalled(string configured, string expected)
    {
        var installed = new[] { Installed(Standard), Installed(Compact) };

        Assert.Equal(expected, Siglip2EmbeddingProvider.ChooseTier(installed, catalog, ExecutionProvider.Cpu, configured)?.TierId);
    }

    [Fact]
    public void ChooseTier_NothingInstalled_IsNull()
    {
        Assert.Null(Siglip2EmbeddingProvider.ChooseTier([], catalog, ExecutionProvider.Cpu, null));
    }

    [Theory]
    [InlineData(ExecutionProvider.Cpu, Standard)]
    [InlineData(ExecutionProvider.DirectMl, Compact)]
    public async Task Provider_BothInstalled_UsesTheTierForTheMachine(ExecutionProvider machine, string expected)
    {
        var tiers = new Dictionary<string, InstalledModel>
        {
            [Standard] = await ImportAsync(Standard),
            [Compact] = await ImportAsync(Compact),
        };
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(hardware: new FixedHardware(machine)), new Siglip2Options(), catalog);

        await provider.CheckHealthAsync(Token);

        Assert.Equal(tiers[expected].ContentHash, provider.Model.FileHash);
    }

    [Fact]
    public async Task Provider_StoreChanges_ChoosesAgain()
    {
        var standard = await ImportAsync(Standard);
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(hardware: new FixedHardware(ExecutionProvider.DirectMl)), new Siglip2Options(), catalog);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(standard.ContentHash, provider.Model.FileHash);

        var compact = await ImportAsync(Compact);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(compact.ContentHash, provider.Model.FileHash);

        await store.DeleteAsync(Siglip2Model.ModelId, Siglip2Model.Version, Compact, confirmed: true, Token);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(standard.ContentHash, provider.Model.FileHash);
    }

    [Fact]
    public async Task Provider_GpuTierFailsToLoad_SwitchesToTheCpuTierForGood()
    {
        var standard = await ImportAsync(Standard);
        var compact = await ImportAsync(Compact);
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(hardware: new FixedHardware(ExecutionProvider.DirectMl)), new Siglip2Options(), catalog);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(compact.ContentHash, provider.Model.FileHash);

        // The placeholder files don't load either tier, but the failure of the GPU tier makes the provider move to the CPU tier.
        await Assert.ThrowsAsync<AiProviderException>(() => provider.EmbedTextsAsync(["a cat"], Token));
        await provider.CheckHealthAsync(Token);

        Assert.Equal(standard.ContentHash, provider.Model.FileHash);
    }

    [Fact]
    public async Task Provider_GpuTierFailsToLoad_WithNoCpuTierInstalled_StaysAndThrows()
    {
        var compact = await ImportAsync(Compact);
        using var provider = new Siglip2EmbeddingProvider(store, new OnnxSessionFactory(hardware: new FixedHardware(ExecutionProvider.DirectMl)), new Siglip2Options(), catalog);

        await Assert.ThrowsAsync<AiProviderException>(() => provider.EmbedTextsAsync(["a cat"], Token));
        await provider.CheckHealthAsync(Token);

        Assert.Equal(compact.ContentHash, provider.Model.FileHash);
    }

    public void Dispose()
    {
        store.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Provider_HardwareOverrides_WinAndApplyToTheNextCall()
    {
        var standard = await ImportAsync(Standard);
        var compact = await ImportAsync(Compact);
        var overrides = new FakeOverrides();
        using var provider = new Siglip2EmbeddingProvider(
            store, new OnnxSessionFactory(hardware: new FixedHardware(ExecutionProvider.DirectMl)), new Siglip2Options(), catalog, overrides);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(compact.ContentHash, provider.Model.FileHash);

        overrides.Set(provider: null, tierId: Standard);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(standard.ContentHash, provider.Model.FileHash);

        // The CPU as the provider: the tier that suits it.
        overrides.Set(ExecutionProvider.Cpu, tierId: null);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(standard.ContentHash, provider.Model.FileHash);

        overrides.Set(provider: null, tierId: null);
        await provider.CheckHealthAsync(Token);
        Assert.Equal(compact.ContentHash, provider.Model.FileHash);
    }

    [Fact]
    public async Task Provider_OverriddenTierNotInstalled_FallsBackToTheAutomaticChoice()
    {
        var standard = await ImportAsync(Standard);
        var overrides = new FakeOverrides();
        overrides.Set(provider: null, tierId: Compact);
        using var provider = new Siglip2EmbeddingProvider(
            store, new OnnxSessionFactory(hardware: new FixedHardware(ExecutionProvider.DirectMl)), new Siglip2Options(), catalog, overrides);

        await provider.CheckHealthAsync(Token);

        Assert.Equal(standard.ContentHash, provider.Model.FileHash);
    }

    private static InstalledModel Installed(string tierId) =>
        new(Siglip2Model.ModelId, Siglip2Model.Version, tierId, "SigLIP 2", null, null, "models", [], tierId, DateTimeOffset.UnixEpoch);

    /// <summary>Imports a tier with placeholder files under the catalogue's names; the tier choice never reads them.</summary>
    private async Task<InstalledModel> ImportAsync(string tierId)
    {
        var tier = catalog.Find(Siglip2Model.ModelId)!.GetTier(tierId);
        var files = new List<ModelFileSource>();
        foreach (var file in tier.Files)
        {
            var path = Path.Combine(root, tierId + "-" + file.FileName);
            await File.WriteAllTextAsync(path, tierId + file.Role, Token);
            files.Add(new ModelFileSource(file.Role, path, FileName: file.FileName));
        }

        return await store.ImportAsync(new ModelImportRequest(Siglip2Model.ModelId, Siglip2Model.Version, tierId, "SigLIP 2", files), null, Token);
    }

    private sealed class FixedHardware(ExecutionProvider provider) : IHardwareInfo
    {
        public HardwareProfile Profile { get; } = new("X64", 8, 16L << 30, []);

        public ExecutionProvider PreferredExecutionProvider => provider;

        public GpuInfo? PreferredGpu => null;

        public int DefaultParallelism => 4;
    }

    private sealed class FakeOverrides : IHardwareOverrides
    {
        public event EventHandler? Changed;

        public ExecutionProvider? ExecutionProvider { get; private set; }

        public string? TierId { get; private set; }

        public void Set(ExecutionProvider? provider, string? tierId)
        {
            ExecutionProvider = provider;
            TierId = tierId;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
