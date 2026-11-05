using System.Diagnostics;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI;
using MediaFileClassifier.Testing.AI;

namespace MediaFileClassifier.Infrastructure.Tests.AI;

/// <summary>
/// Tests for the fake providers in <c>MediaFileClassifier.Testing</c> that E06's pipeline tests use.
/// </summary>
public sealed class FakeProvidersTests
{
    private static readonly AiImage Dog = new(new byte[] { 1, 2, 3 }, "image/jpeg", "dog");
    private static readonly AiImage Cat = new(new byte[] { 4, 5, 6 }, "image/jpeg", "cat");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryCapability_HasADeterministicFake()
    {
        var describer = new FakeImageDescriber();
        var ocr = new FakeOcrProvider(recognize: image => $"text of {image.Name}");
        var transcriber = new FakeTranscriptionProvider();

        var first = await describer.DescribeAsync(Dog, ImageDescriptionOptions.Default, Token);
        var second = await new FakeImageDescriber().DescribeAsync(Dog, ImageDescriptionOptions.Default, Token);

        Assert.Equal(first.Value.Title, second.Value.Title);
        Assert.Equal(first.Value.Tags, second.Value.Tags);
        Assert.Equal("fake-1", first.PromptVersion);
        Assert.Equal("text of dog", (await ocr.RecognizeTextAsync(Dog, Token)).Value.Text);
        Assert.Equal("Speech in clip.wav.", (await transcriber.TranscribeAsync(new AiAudio(@"C:\clip.wav", "audio/wav"), Token)).Value.Text);
    }

    [Fact]
    public async Task Embeddings_AreNormalizedRepeatableAndShareOneSpace()
    {
        var embedder = new FakeEmbeddingProvider(dimension: 16, imageKey: image => image.Name!);

        var images = await embedder.EmbedImagesAsync([Dog, Cat], Token);
        var query = await embedder.EmbedTextAsync("DOG", Token);

        Assert.Equal(images.Model, query.Model);
        Assert.Equal(images.Value[0].ToArray(), query.Value.ToArray());
        Assert.NotEqual(images.Value[1].ToArray(), query.Value.ToArray());
        Assert.All(images.Value, vector => Assert.Equal(1, MathF.Sqrt(vector.ToArray().Sum(value => value * value)), 4));
        Assert.Equal(16, query.Value.Length);
        Assert.Equal(FakeEmbeddingProvider.VectorFor("dog", 16), query.Value.ToArray());
    }

    [Fact]
    public async Task Embeddings_DifferentModelName_IsADifferentModel()
    {
        var oldModel = new FakeEmbeddingProvider();
        var newModel = new FakeEmbeddingProvider(modelName: "fake-embedder-2");

        Assert.NotEqual(oldModel.Model, newModel.Model);
        Assert.NotEqual(oldModel.Id, newModel.Id);
        Assert.Equal((await oldModel.EmbedTextAsync("dog", Token)).Value.ToArray(), (await newModel.EmbedTextAsync("dog", Token)).Value.ToArray());
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 100)]
    public async Task FailureRate_ZeroAndOne_AreExact(double rate, int expectedFailures)
    {
        var ocr = new FakeOcrProvider(new FakeProviderBehavior { FailureRate = rate });

        Assert.Equal(expectedFailures, await CountFailuresAsync(ocr, 100));
        Assert.Equal(100, ocr.Calls);
    }

    [Fact]
    public async Task FailureRate_Partial_IsRepeatableForASeedAndRoughlyTheRate()
    {
        var behavior = new FakeProviderBehavior { FailureRate = 0.3, Seed = 7 };

        var first = await FailurePatternAsync(new FakeOcrProvider(behavior), 200);
        var second = await FailurePatternAsync(new FakeOcrProvider(behavior), 200);
        var otherSeed = await FailurePatternAsync(new FakeOcrProvider(behavior with { Seed = 8 }), 200);

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherSeed);
        Assert.InRange(first.Count(failed => failed), 40, 80);
    }

    [Fact]
    public async Task Failure_HasTheConfiguredKind()
    {
        var describer = new FakeImageDescriber(FakeProviderBehavior.AlwaysFailing(AiFailureKind.InvalidOutput));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => describer.DescribeAsync(Dog, ImageDescriptionOptions.Default, Token));

        Assert.Equal(AiFailureKind.InvalidOutput, exception.Kind);
        Assert.Equal(describer.Id, exception.ProviderId);
        Assert.NotNull(exception.RawOutput);
    }

    [Fact]
    public async Task Latency_DelaysTheCallAndIsReportedAsDuration()
    {
        var ocr = new FakeOcrProvider(new FakeProviderBehavior { Latency = TimeSpan.FromMilliseconds(60) });

        var stopwatch = Stopwatch.StartNew();
        var output = await ocr.RecognizeTextAsync(Dog, Token);

        Assert.True(stopwatch.ElapsedMilliseconds >= 50, $"Took {stopwatch.ElapsedMilliseconds} ms.");
        Assert.Equal(TimeSpan.FromMilliseconds(60), output.Duration);
    }

    [Fact]
    public async Task Latency_CanBeCancelled()
    {
        var ocr = new FakeOcrProvider(new FakeProviderBehavior { Latency = TimeSpan.FromMinutes(5) });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ocr.RecognizeTextAsync(Dog, cancellation.Token));
    }

    [Fact]
    public async Task Registry_FindsFakesByCapabilityAndReportsTheirHealth()
    {
        var embedder = new FakeEmbeddingProvider();
        var missing = new FakeImageDescriber(new FakeProviderBehavior
        {
            Health = new ProviderHealth(ProviderHealthStatus.ModelMissing, "Not installed."),
        });
        var registry = new AiProviderRegistry([embedder, missing], TimeProvider.System);

        var statuses = await registry.CheckHealthAsync(Token);

        Assert.Same(embedder, registry.FindTextEmbedderFor(Assert.Single(registry.GetProviders<IImageEmbeddingProvider>()).Model));
        Assert.True(statuses[0].IsAvailable);
        Assert.True(statuses[1].Health.IsWaitingForModel);
    }

    private static async Task<int> CountFailuresAsync(FakeOcrProvider ocr, int calls) =>
        (await FailurePatternAsync(ocr, calls)).Count(failed => failed);

    private static async Task<List<bool>> FailurePatternAsync(FakeOcrProvider ocr, int calls)
    {
        var pattern = new List<bool>();
        for (var index = 0; index < calls; index++)
        {
            try
            {
                await ocr.RecognizeTextAsync(Dog, Token);
                pattern.Add(false);
            }
            catch (AiProviderException)
            {
                pattern.Add(true);
            }
        }

        return pattern;
    }
}
