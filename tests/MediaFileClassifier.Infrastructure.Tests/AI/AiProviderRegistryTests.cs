using System.Net;
using System.Net.Sockets;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Infrastructure.AI.OpenAiCompatible;
using MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

namespace MediaFileClassifier.Infrastructure.Tests.AI;

/// <summary>
/// Tests for <see cref="AiProviderRegistry"/>.
/// </summary>
public sealed class AiProviderRegistryTests
{
    private static readonly ModelIdentity SharedModel = new(ProviderIds.Fake, "two-tower", embeddingDimension: 2);

    [Fact]
    public async Task CheckHealth_MissingModelFileAndUnreachableEndpoint_AreReported()
    {
        using var onnx = new TinyTextEmbedder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.onnx"), 4);
        var endpoint = new OpenAiCompatibleEndpoint(new Uri($"http://127.0.0.1:{GetClosedPort()}/v1"), "google/gemma-4-e2b") { MaxRetries = 0 };
        using var http = new OpenAiCompatibleOcrProvider("lmstudio:ocr", "LM Studio OCR", endpoint);
        var registry = CreateRegistry(onnx, http);

        var statuses = await registry.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.ModelMissing, statuses[0].Health.Status);
        Assert.Equal(ProviderHealthStatus.Unreachable, statuses[1].Health.Status);
        Assert.All(statuses, status => Assert.False(status.IsAvailable));
        Assert.Equal(ProviderHealthStatus.ModelMissing, registry.GetLastHealth(onnx.Id)!.Health.Status);
        Assert.Equal(ProviderHealthStatus.Unreachable, registry.GetLastHealth(http.Id)!.Health.Status);
    }

    [Fact]
    public async Task CheckHealth_ReportsCapabilitiesAndCheckTime()
    {
        var registry = CreateRegistry(Stub("both", SharedModel, image: true, text: true));

        var status = Assert.Single(await registry.CheckHealthAsync(TestContext.Current.CancellationToken));

        Assert.True(status.IsAvailable);
        Assert.Equal([AiCapability.ImageEmbedding, AiCapability.TextEmbedding], status.Capabilities);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), status.CheckedAt);
    }

    [Fact]
    public async Task CheckHealth_UnsupportedPlatform_IsReportedWithoutCallingTheProvider()
    {
        var provider = Stub("windows-only", SharedModel, image: true, supported: false);
        var registry = CreateRegistry(provider);

        var status = await registry.CheckHealthAsync(provider.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.NotSupportedOnPlatform, status.Health.Status);
        Assert.Equal(0, provider.HealthChecks);
    }

    [Fact]
    public async Task CheckHealth_ProviderThrows_IsUnhealthyInsteadOfFailing()
    {
        var registry = CreateRegistry(Stub("broken", SharedModel, image: true, healthException: new InvalidOperationException("boom")));

        var status = Assert.Single(await registry.CheckHealthAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProviderHealthStatus.Unhealthy, status.Health.Status);
        Assert.Equal("boom", status.Health.Detail);
    }

    [Fact]
    public void GetProviders_ByCapability_SkipsOtherCapabilitiesAndUnsupportedPlatforms()
    {
        var image = Stub("image", SharedModel, image: true);
        var text = Stub("text", SharedModel, text: true);
        var unsupported = Stub("unsupported", SharedModel, image: true, supported: false);
        var registry = CreateRegistry(image, text, unsupported);

        Assert.Equal([image], registry.GetProviders<IImageEmbeddingProvider>().Cast<IAiProvider>());
        Assert.Equal([text], registry.GetProviders<ITextEmbeddingProvider>().Cast<IAiProvider>());
        Assert.Empty(registry.GetProviders<IImageDescriber>());
        Assert.Equal(3, registry.Providers.Count);
    }

    [Fact]
    public void FindTextEmbedderFor_MatchesOnlyTheSameModel()
    {
        var other = Stub("other-text", new ModelIdentity(ProviderIds.Fake, "other", embeddingDimension: 2), text: true);
        var same = Stub("same-text", SharedModel, text: true);
        var registry = CreateRegistry(other, same);

        Assert.Same(same, registry.FindTextEmbedderFor(SharedModel));
        Assert.Null(registry.FindTextEmbedderFor(new ModelIdentity(ProviderIds.Fake, "unknown", embeddingDimension: 2)));
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        var registry = CreateRegistry(Stub("a", SharedModel, image: true));

        Assert.Throws<ArgumentException>(() => registry.Register(Stub("a", SharedModel, text: true)));
    }

    [Fact]
    public async Task Unregister_RemovesProviderAndItsHealth()
    {
        var provider = Stub("a", SharedModel, image: true);
        var registry = CreateRegistry();
        registry.Register(provider);
        await registry.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.True(registry.Unregister("a"));
        Assert.False(registry.Unregister("a"));
        Assert.Null(registry.Find("a"));
        Assert.Null(registry.GetLastHealth("a"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => registry.CheckHealthAsync("a", TestContext.Current.CancellationToken));
    }

    private static AiProviderRegistry CreateRegistry(params IAiProvider[] providers) =>
        new(providers, new FixedTimeProvider(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));

    private static int GetClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static StubProvider Stub(
        string id,
        ModelIdentity model,
        bool image = false,
        bool text = false,
        bool supported = true,
        Exception? healthException = null) => (image, text) switch
        {
            (true, true) => new StubImageAndText(id, model, supported, healthException),
            (true, false) => new StubImage(id, model, supported, healthException),
            (false, true) => new StubText(id, model, supported, healthException),
            _ => throw new ArgumentException("A stub needs a capability."),
        };

    private abstract class StubProvider(string id, ModelIdentity model, bool supported, Exception? healthException) : IAiProvider
    {
        public string Id => id;

        public string DisplayName => id;

        public AiProviderKind Kind => AiProviderKind.Fake;

        public ModelIdentity Model => model;

        public bool IsSupportedOnCurrentPlatform => supported;

        public int HealthChecks { get; private set; }

        public int Dimension => 2;

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            HealthChecks++;
            return healthException is null ? Task.FromResult(ProviderHealth.Healthy) : throw healthException;
        }

        public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedImagesAsync(IReadOnlyList<AiImage> images, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubImage(string id, ModelIdentity model, bool supported, Exception? healthException)
        : StubProvider(id, model, supported, healthException), IImageEmbeddingProvider;

    private sealed class StubText(string id, ModelIdentity model, bool supported, Exception? healthException)
        : StubProvider(id, model, supported, healthException), ITextEmbeddingProvider;

    private sealed class StubImageAndText(string id, ModelIdentity model, bool supported, Exception? healthException)
        : StubProvider(id, model, supported, healthException), IImageEmbeddingProvider, ITextEmbeddingProvider;
}
