using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.AI;

namespace MediaFileClassifier.Infrastructure.Tests.AI;

/// <summary>
/// Tests for <see cref="ModelIdentity"/> and <see cref="AiProviderExtensions.GetCapabilities"/>.
/// </summary>
public sealed class ModelIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_NormalizesHashAndBlankVersion()
    {
        var identity = new ModelIdentity(ProviderIds.Onnx, "siglip2", " ", "ABCDEF", 768);

        Assert.Null(identity.Version);
        Assert.Equal("abcdef", identity.FileHash);
        Assert.Equal(new ModelIdentity(ProviderIds.Onnx, "siglip2", null, "abcdef", 768), identity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveDimension_Throws(int dimension)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelIdentity(ProviderIds.Onnx, "siglip2", embeddingDimension: dimension));
    }

    [Fact]
    public void ToRegistration_RoundTripsThroughMatches()
    {
        var identity = new ModelIdentity(ProviderIds.Onnx, "siglip2", "1", "abc", 768);

        var registration = identity.ToRegistration(Now);

        Assert.True(identity.Matches(registration));
        Assert.Equal(Now, registration.RegisteredAt);
    }

    [Theory]
    [InlineData("openai-compatible", "siglip2", "1", "abc", 768)]
    [InlineData("onnx", "siglip2-large", "1", "abc", 768)]
    [InlineData("onnx", "siglip2", "2", "abc", 768)]
    [InlineData("onnx", "siglip2", "1", "abd", 768)]
    [InlineData("onnx", "siglip2", "1", "abc", 512)]
    public void Matches_AnyFieldDiffers_IsFalse(string provider, string name, string version, string hash, int dimension)
    {
        var registration = new ModelIdentity(ProviderIds.Onnx, "siglip2", "1", "abc", 768).ToRegistration(Now);

        Assert.False(new ModelIdentity(provider, name, version, hash, dimension).Matches(registration));
    }

    [Fact]
    public void Matches_HashCaseDiffers_IsTrue()
    {
        var registration = ModelRegistration.Create(ProviderIds.Onnx, "siglip2", null, "ABC", null, Now);

        Assert.True(new ModelIdentity(ProviderIds.Onnx, "siglip2", fileHash: "abc").Matches(registration));
    }

    [Fact]
    public void GetCapabilities_ListsOnlyImplementedInterfaces()
    {
        Assert.Equal([AiCapability.Ocr, AiCapability.TextEmbedding], new OcrAndTextProvider().GetCapabilities());
    }

    private sealed class OcrAndTextProvider : IOcrProvider, ITextEmbeddingProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public AiProviderKind Kind => AiProviderKind.Fake;

        public ModelIdentity Model { get; } = new(ProviderIds.Fake, "test", embeddingDimension: 2);

        public bool IsSupportedOnCurrentPlatform => true;

        public int Dimension => 2;

        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(ProviderHealth.Healthy);

        public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AiOutput<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
