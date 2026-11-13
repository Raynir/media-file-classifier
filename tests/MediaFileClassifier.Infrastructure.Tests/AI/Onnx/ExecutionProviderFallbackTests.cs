using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.Hardware;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Onnx;

/// <summary>
/// Forces each failure path of a GPU execution provider and checks that the model falls back to the CPU (E06-S08-T01).
/// The test runtime is the CPU build, so "DirectML" is a CPU session the factory labels as DirectML.
/// </summary>
public sealed class ExecutionProviderFallbackTests : IDisposable
{
    private const int Width = 4;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "mfc-tests", Guid.NewGuid().ToString("N"));
    private readonly string modelPath;

    public ExecutionProviderFallbackTests()
    {
        Directory.CreateDirectory(directory);
        modelPath = Path.Combine(directory, "tiny.onnx");
        TinyOnnxModel.WriteIdentity(modelPath, Width);
    }

    [Fact]
    public void Create_GpuProviderLoads_RunsOnIt()
    {
        var factory = CreateFactory(append: (_, _, _) => { });

        using var session = factory.Create(modelPath, ExecutionProvider.DirectMl);

        Assert.Equal(ExecutionProvider.DirectMl, session.ExecutionProvider);
    }

    [Theory]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(EntryPointNotFoundException))]
    [InlineData(typeof(InvalidOperationException))]
    public void Create_GpuProviderFailsToInitialise_FallsBackToCpu(Type failure)
    {
        var factory = CreateFactory(append: (_, _, _) => throw (Exception)Activator.CreateInstance(failure, "no GPU")!);

        using var session = factory.Create(modelPath, ExecutionProvider.DirectMl);

        Assert.Equal(ExecutionProvider.Cpu, session.ExecutionProvider);
    }

    [Fact]
    public void Create_ModelDoesNotLoadOnCpuEither_Throws()
    {
        File.WriteAllBytes(modelPath, [1, 2, 3, 4]);
        var factory = CreateFactory(append: (_, _, _) => { });

        Assert.Throws<OnnxRuntimeException>(() => factory.Create(modelPath, ExecutionProvider.DirectMl));
    }

    [Fact]
    public void Create_DirectMl_UsesThePreferredAdapter()
    {
        var devices = new List<int>();
        var hardware = new FakeHardwareInfo(ExecutionProvider.DirectMl, new GpuInfo("Discrete", "NVIDIA", 8L << 30, AdapterIndex: 1));
        var factory = CreateFactory(append: (_, _, device) => devices.Add(device), hardware);

        using var session = factory.Create(modelPath, factory.DefaultExecutionProvider);

        Assert.Equal(ExecutionProvider.DirectMl, session.ExecutionProvider);
        Assert.Equal([1], devices);
    }

    [Fact]
    public void DefaultExecutionProvider_WithoutHardware_IsCpu()
    {
        Assert.Equal(ExecutionProvider.Cpu, new OnnxSessionFactory().DefaultExecutionProvider);
    }

    [Fact]
    public void Run_FirstRunFailsOnGpu_MovesToCpuAndRetries()
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 1);

        var vector = provider.Embed();

        Assert.Equal(Width, vector.Length);
        Assert.Equal(ExecutionProvider.Cpu, provider.LoadedExecutionProvider);
        Assert.Equal(2, provider.Attempts);
    }

    [Fact]
    public void Run_AfterFirstRunFailedOnGpu_LaterSessionsStartOnCpu()
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using (var first = new FlakyEmbedder(modelPath, factory, failingRuns: 1))
        {
            first.Embed();
        }

        using var second = new FlakyEmbedder(modelPath, factory, failingRuns: 0);

        Assert.Equal(ExecutionProvider.Cpu, second.LoadedExecutionProvider);
    }

    [Fact]
    public void Run_FailsOnCpuAfterMovingThere_Throws()
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 2);

        Assert.Throws<OnnxRuntimeException>(() => provider.Embed());
        Assert.Equal(ExecutionProvider.Cpu, provider.LoadedExecutionProvider);

        // The input was bad, not the GPU: a new session of the file tries the GPU again.
        using var next = new FlakyEmbedder(modelPath, factory, failingRuns: 0);
        Assert.Equal(ExecutionProvider.DirectMl, next.LoadedExecutionProvider);
    }

    [Fact]
    public void Run_FailsOnGpuAfterAGoodRun_IsThrownAndStaysOnGpu()
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 0);
        provider.Embed();

        provider.FailNext = true;

        Assert.Throws<OnnxRuntimeException>(() => provider.Embed());
        Assert.Equal(ExecutionProvider.DirectMl, provider.LoadedExecutionProvider);
    }

    [Fact]
    public void Run_OnCpu_FailureIsThrownWithoutRetry()
    {
        using var provider = new FlakyEmbedder(modelPath, new OnnxSessionFactory(), failingRuns: 1, preferred: ExecutionProvider.Cpu);

        Assert.Throws<OnnxRuntimeException>(() => provider.Embed());
        Assert.Equal(1, provider.Attempts);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private static OnnxSessionFactory CreateFactory(Action<SessionOptions, ExecutionProvider, int> append, IHardwareInfo? hardware = null) =>
        new(hardware: hardware)
        {
            AvailableProviders = () => [ExecutionProvider.Cpu, ExecutionProvider.DirectMl],
            AppendExecutionProvider = append,
        };

    /// <summary>
    /// An identity model whose first runs use a wrong input name, which ONNX Runtime rejects.
    /// </summary>
    private sealed class FlakyEmbedder(string modelPath, OnnxSessionFactory factory, int failingRuns, ExecutionProvider preferred = ExecutionProvider.DirectMl)
        : OnnxModelProvider(
            "onnx:flaky",
            "Flaky",
            new ModelIdentity(ProviderIds.Onnx, "flaky", "1", embeddingDimension: Width),
            new Dictionary<string, string> { ["text"] = modelPath },
            factory,
            preferred)
    {
        private int remainingFailures = failingRuns;

        public int Attempts { get; private set; }

        public bool FailNext { get; set; }

        public ExecutionProvider LoadedExecutionProvider => GetSession("text").ExecutionProvider;

        public float[] Embed() => Run("text", session =>
        {
            Attempts++;
            var fail = FailNext || remainingFailures > 0;
            remainingFailures--;
            FailNext = false;
            var input = new DenseTensor<float>(new float[] { 1, 2, 3, 4 }, [1, Width]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(fail ? "wrong" : TinyOnnxModel.InputName, input)]);
            return results[0].AsEnumerable<float>().ToArray();
        });
    }
}
