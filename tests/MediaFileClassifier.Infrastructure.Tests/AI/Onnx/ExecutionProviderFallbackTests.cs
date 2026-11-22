using System.Reflection;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using MediaFileClassifier.Testing.Files;
using MediaFileClassifier.Testing.Hardware;
using Microsoft.Extensions.Logging;
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

    private readonly TemporaryFolder temp = new();
    private readonly string modelPath;

    public ExecutionProviderFallbackTests()
    {
        Directory.CreateDirectory(temp.Path);
        modelPath = temp.Combine("tiny.onnx");
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

    [Fact]
    public void Run_GpuDeviceLostAfterAGoodRun_MovesToCpuAndRetries()
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 0);
        provider.Embed();

        provider.FailNextWith = CreateRuntimeException("887A0005 The GPU device instance has been suspended.");
        var vector = provider.Embed();

        Assert.Equal(Width, vector.Length);
        Assert.Equal(ExecutionProvider.Cpu, provider.LoadedExecutionProvider);

        // The device is gone, so new sessions of the file start on the CPU.
        using var next = new FlakyEmbedder(modelPath, factory, failingRuns: 0);
        Assert.Equal(ExecutionProvider.Cpu, next.LoadedExecutionProvider);
    }

    [Theory]
    [InlineData("Exception(5) tid(1470) 887A0005 The GPU device instance has been suspended.", true)]
    [InlineData("Exception(2) tid(b2a8) 887A0006 The GPU will not respond to more commands", true)]
    [InlineData("887a0007 reset", true)]
    [InlineData("Invalid input name: wrong", false)]
    public void IsDeviceLost_MatchesTheDxgiDeviceLostCodes(string message, bool expected)
    {
        Assert.Equal(expected, OnnxModelProvider.IsDeviceLost(CreateRuntimeException(message)));
    }

    [Theory]
    [InlineData(ExecutionProvider.DirectMl, 1)]
    [InlineData(ExecutionProvider.Cpu, 2)]
    public async Task Run_FromTwoThreads_OverlapsOnlyOnTheCpu(ExecutionProvider preferred, int expectedOverlap)
    {
        var factory = CreateFactory(append: (_, _, _) => { });
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 0, preferred);
        provider.Embed();

        // Each run waits for the other to enter, so overlapping runs meet and serialised ones time out. The runs get
        // their own threads, so a busy thread pool can't start the second one too late.
        using var bothInside = new CountdownEvent(2);
        provider.InsideRun = () =>
        {
            bothInside.Signal();
            bothInside.Wait(TimeSpan.FromSeconds(expectedOverlap == 1 ? 1 : 10), TestContext.Current.CancellationToken);
        };
        await Task.WhenAll(
            Task.Factory.StartNew(provider.Embed, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default),
            Task.Factory.StartNew(provider.Embed, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default));

        Assert.Equal(expectedOverlap, provider.MaxOverlap);
    }

    [Fact]
    public void Run_LogsTheModelAndInputAtDebugBeforeTheRun()
    {
        var logger = new ListLogger();
        var factory = new OnnxSessionFactory(logger)
        {
            AvailableProviders = () => [ExecutionProvider.Cpu, ExecutionProvider.DirectMl],
            AppendExecutionProvider = (_, _, _) => { },
        };
        using var provider = new FlakyEmbedder(modelPath, factory, failingRuns: 0)
        {
            InsideRun = () => logger.Add(LogLevel.Trace, "inside the run"),
        };

        provider.Embed();

        var debug = logger.Entries.FindIndex(entry => entry.Level == LogLevel.Debug);
        Assert.True(debug >= 0, "No Debug line was logged.");
        Assert.Equal("Running tiny.onnx on DirectMl: 1 x 4 vector", logger.Entries[debug].Message);
        Assert.True(debug < logger.Entries.FindIndex(entry => entry.Message == "inside the run"));
    }

    public void Dispose()
    {
        temp.Dispose();
    }

    private static OnnxSessionFactory CreateFactory(Action<SessionOptions, ExecutionProvider, int> append, IHardwareInfo? hardware = null) =>
        new(hardware: hardware)
        {
            AvailableProviders = () => [ExecutionProvider.Cpu, ExecutionProvider.DirectMl],
            AppendExecutionProvider = append,
        };

    private sealed class ListLogger : ILogger<OnnxSessionFactory>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Add(logLevel, formatter(state, exception));

        public void Add(LogLevel level, string message)
        {
            lock (Entries)
            {
                Entries.Add((level, message));
            }
        }
    }

    private static OnnxRuntimeException CreateRuntimeException(string message)
    {
        // The (ErrorCode, string) constructor and its enum are internal to ONNX Runtime (written against 1.31.1); an
        // upgrade that changes them fails here at run time.
        var constructor = typeof(OnnxRuntimeException).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(candidate => candidate.GetParameters() is [{ ParameterType.IsEnum: true }, { ParameterType: var text }] && text == typeof(string));
        var code = Enum.ToObject(constructor.GetParameters()[0].ParameterType, 1);
        return (OnnxRuntimeException)constructor.Invoke([code, message]);
    }

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
        private int inside;
        private int maxOverlap;

        public int Attempts { get; private set; }

        public bool FailNext { get; set; }

        public OnnxRuntimeException? FailNextWith { get; set; }

        public Action? InsideRun { get; set; }

        public int MaxOverlap => Volatile.Read(ref maxOverlap);

        public ExecutionProvider LoadedExecutionProvider => GetSession("text").ExecutionProvider;

        public float[] Embed() => Run(
            "text",
            session =>
        {
            Attempts++;
            if (FailNextWith is { } error)
            {
                FailNextWith = null;
                throw error;
            }

            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxOverlap, now);
            try
            {
                InsideRun?.Invoke();
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }

            var fail = FailNext || remainingFailures > 0;
            remainingFailures--;
            FailNext = false;
            var input = new DenseTensor<float>(new float[] { 1, 2, 3, 4 }, [1, Width]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(fail ? "wrong" : TinyOnnxModel.InputName, input)]);
            return results[0].AsEnumerable<float>().ToArray();
        },
            "1 x 4 vector");

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
