using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.Files;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

/// <summary>
/// Tests for <see cref="OnnxModelProvider"/> and <see cref="OnnxSessionFactory"/>, with a minimal working example: a
/// text embedder whose "model" is an identity graph over hand-made features.
/// </summary>
public sealed class OnnxModelProviderTests : IDisposable
{
    private const int Width = 4;

    private readonly TemporaryFolder temp = new();

    public OnnxModelProviderTests()
    {
        Directory.CreateDirectory(temp.Path);
    }

    [Fact]
    public async Task EmbedTexts_TinyModel_RunsOnCpuAndRecordsTheModel()
    {
        using var provider = CreateProvider(writeModel: true);

        var output = await provider.EmbedTextsAsync(["abc", "a b"], TestContext.Current.CancellationToken);

        Assert.Equal(2, output.Value.Count);
        Assert.All(output.Value, vector => Assert.Equal(Width, vector.Length));
        Assert.Equal(1, Length(output.Value[0].Span), 5);
        Assert.Equal(provider.Model, output.Model);
        Assert.Equal(provider.Id, output.ProviderId);
        Assert.Equal(ExecutionProvider.Cpu, provider.LoadedExecutionProvider);
    }

    [Fact]
    public async Task CheckHealth_ModelFileMissing_ReportsModelMissing()
    {
        using var provider = CreateProvider(writeModel: false);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderHealthStatus.ModelMissing, health.Status);
        Assert.True(health.IsWaitingForModel);
        Assert.Contains("tiny", health.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealth_ModelPresent_IsHealthy()
    {
        using var provider = CreateProvider(writeModel: true);

        Assert.True((await provider.CheckHealthAsync(TestContext.Current.CancellationToken)).IsHealthy);
    }

    [Fact]
    public async Task EmbedTexts_ModelFileMissing_ThrowsModelMissing()
    {
        using var provider = CreateProvider(writeModel: false);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.EmbedTextsAsync(["abc"], TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.ModelMissing, exception.Kind);
    }

    [Fact]
    public async Task EmbedTexts_CorruptModelFile_ThrowsProviderError()
    {
        await File.WriteAllTextAsync(temp.Combine("tiny.onnx"), "not a model", TestContext.Current.CancellationToken);
        using var provider = CreateProvider(writeModel: false);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.EmbedTextsAsync(["abc"], TestContext.Current.CancellationToken));

        Assert.Equal(AiFailureKind.ProviderError, exception.Kind);
    }

    [Fact]
    public void GetAvailableProviders_AlwaysIncludesCpu()
    {
        Assert.Contains(ExecutionProvider.Cpu, OnnxSessionFactory.GetAvailableProviders());
    }

    [Theory]
    [InlineData(ExecutionProvider.Cpu, new[] { ExecutionProvider.Cpu, ExecutionProvider.DirectMl }, new[] { ExecutionProvider.Cpu })]
    [InlineData(ExecutionProvider.DirectMl, new[] { ExecutionProvider.Cpu, ExecutionProvider.DirectMl }, new[] { ExecutionProvider.DirectMl, ExecutionProvider.Cpu })]
    [InlineData(ExecutionProvider.DirectMl, new[] { ExecutionProvider.Cpu }, new[] { ExecutionProvider.Cpu })]
    [InlineData(ExecutionProvider.Cuda, new[] { ExecutionProvider.Cpu, ExecutionProvider.Cuda }, new[] { ExecutionProvider.Cuda, ExecutionProvider.Cpu })]
    public void GetAttemptOrder_PrefersAvailableProviderThenCpu(ExecutionProvider preferred, ExecutionProvider[] available, ExecutionProvider[] expected)
    {
        Assert.Equal(expected, OnnxSessionFactory.GetAttemptOrder(preferred, available));
    }

    [Fact]
    public void Create_PreferredProviderNotInRuntime_FallsBackToCpu()
    {
        var path = temp.Combine("tiny.onnx");
        TinyOnnxModel.WriteIdentity(path, Width);

        // The CPU test runtime has no DirectML.
        using var session = new OnnxSessionFactory().Create(path, ExecutionProvider.DirectMl);

        Assert.Equal(ExecutionProvider.Cpu, session.ExecutionProvider);
    }

    public void Dispose()
    {
        temp.Dispose();
    }

    private static float Length(ReadOnlySpan<float> vector)
    {
        var sum = 0f;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        return MathF.Sqrt(sum);
    }

    private TinyTextEmbedder CreateProvider(bool writeModel)
    {
        var path = temp.Combine("tiny.onnx");
        if (writeModel)
        {
            TinyOnnxModel.WriteIdentity(path, Width);
        }

        return new TinyTextEmbedder(path, Width);
    }
}
