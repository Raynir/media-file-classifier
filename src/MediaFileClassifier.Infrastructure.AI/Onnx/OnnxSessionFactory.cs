using System.Collections.Concurrent;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.AI.Onnx;

/// <summary>
/// Creates ONNX Runtime inference sessions, trying the preferred execution provider first and falling back to the CPU
/// (D010, D015). Which execution providers exist depends on the native runtime the head ships: DirectML on Desktop
/// and the CPU on Android (E06-S08).
/// </summary>
/// <remarks>
/// <para>
/// A GPU provider can fail in three places: it isn't in the runtime, it doesn't initialise (driver, adapter, missing
/// DLL), or the model loads but its first run fails. The first two fall back here; the third is reported by
/// <see cref="OnnxModelProvider"/> through <see cref="ReportRunFailure"/>, and that model file then uses the CPU until
/// the app restarts, so a failed GPU never stops analysis.
/// </para>
/// <para>
/// Every choice is logged with its reason, once per model file and provider.
/// </para>
/// </remarks>
public sealed class OnnxSessionFactory
{
    private static readonly IReadOnlyDictionary<ExecutionProvider, string> RuntimeNames = new Dictionary<ExecutionProvider, string>
    {
        [ExecutionProvider.Cpu] = "CPUExecutionProvider",
        [ExecutionProvider.DirectMl] = "DmlExecutionProvider",
        [ExecutionProvider.Cuda] = "CUDAExecutionProvider",
        [ExecutionProvider.Nnapi] = "NnapiExecutionProvider",
    };

    private readonly ILogger logger;
    private readonly IHardwareInfo? hardware;
    private readonly ConcurrentDictionary<(string Path, ExecutionProvider Provider), string> failedRuns = new();
    private readonly ConcurrentDictionary<(string Path, ExecutionProvider Provider, string Kind), bool> logged = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OnnxSessionFactory"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="hardware">
    /// Detected hardware, which gives the default execution provider and the GPU adapter. Without it, the default is
    /// the CPU and GPU providers use adapter 0.
    /// </param>
    public OnnxSessionFactory(ILogger<OnnxSessionFactory>? logger = null, IHardwareInfo? hardware = null)
    {
        this.logger = logger ?? NullLogger<OnnxSessionFactory>.Instance;
        this.hardware = hardware;
    }

    /// <summary>
    /// Gets the execution provider to try first when the user hasn't chosen one: the hardware's preference, or the CPU.
    /// </summary>
    public ExecutionProvider DefaultExecutionProvider => hardware?.PreferredExecutionProvider ?? ExecutionProvider.Cpu;

    /// <summary>
    /// Gets the providers the runtime has. Tests replace it to pretend a GPU provider exists.
    /// </summary>
    internal Func<IReadOnlyList<ExecutionProvider>> AvailableProviders { get; init; } = GetAvailableProviders;

    /// <summary>
    /// Gets the step that adds an execution provider to the session options. Tests replace it to force a provider to
    /// fail, or to stand in for a GPU provider the test runtime doesn't have.
    /// </summary>
    internal Action<SessionOptions, ExecutionProvider, int> AppendExecutionProvider { get; init; } = AppendToOptions;

    /// <summary>
    /// Lists the execution providers the loaded native runtime supports.
    /// </summary>
    /// <returns>The available providers. The CPU is always among them.</returns>
    /// <exception cref="DllNotFoundException">The native ONNX Runtime isn't present.</exception>
    public static IReadOnlyList<ExecutionProvider> GetAvailableProviders()
    {
        var names = OrtEnv.Instance().GetAvailableProviders();
        return RuntimeNames.Where(pair => names.Contains(pair.Value, StringComparer.Ordinal)).Select(pair => pair.Key).ToList();
    }

    /// <summary>
    /// Builds the order to try execution providers in: the preferred one if the runtime has it, then the CPU.
    /// </summary>
    /// <param name="preferred">Preferred provider, usually <see cref="IHardwareInfo.PreferredExecutionProvider"/>.</param>
    /// <param name="available">Providers the runtime has.</param>
    /// <returns>Providers to try, in order, ending with <see cref="ExecutionProvider.Cpu"/>.</returns>
    public static IReadOnlyList<ExecutionProvider> GetAttemptOrder(ExecutionProvider preferred, IReadOnlyCollection<ExecutionProvider> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        return preferred != ExecutionProvider.Cpu && available.Contains(preferred)
            ? [preferred, ExecutionProvider.Cpu]
            : [ExecutionProvider.Cpu];
    }

    /// <summary>
    /// Creates a session for a model file.
    /// </summary>
    /// <param name="modelPath">Full path of the <c>.onnx</c> file.</param>
    /// <param name="preferred">Execution provider to try first.</param>
    /// <param name="threads">Intra-op thread count, or <c>0</c> for ONNX Runtime's default.</param>
    /// <returns>The session and the execution provider it runs on.</returns>
    /// <exception cref="FileNotFoundException">The model file doesn't exist.</exception>
    /// <exception cref="OnnxRuntimeException">The model doesn't load on the CPU either.</exception>
    public OnnxSession Create(string modelPath, ExecutionProvider preferred, int threads = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The model file doesn't exist.", modelPath);
        }

        var fileName = Path.GetFileName(modelPath);
        var available = AvailableProviders();
        var attempts = GetAttemptOrder(preferred, available);
        if (preferred != ExecutionProvider.Cpu && attempts[0] == ExecutionProvider.Cpu && logged.TryAdd((modelPath, preferred, "missing"), true))
        {
            logger.LogInformation(
                "{ExecutionProvider} isn't in this ONNX Runtime ({Available}), so {Model} uses the CPU",
                preferred,
                string.Join(", ", available),
                fileName);
        }

        if (attempts[0] != ExecutionProvider.Cpu && failedRuns.TryGetValue((modelPath, attempts[0]), out var reason))
        {
            if (logged.TryAdd((modelPath, attempts[0], "failed-earlier"), true))
            {
                logger.LogInformation("{Model} uses the CPU, because it failed on {ExecutionProvider} earlier: {Reason}", fileName, attempts[0], reason);
            }

            attempts = [ExecutionProvider.Cpu];
        }

        for (var index = 0; ; index++)
        {
            var provider = attempts[index];
            SessionOptions? options = null;
            try
            {
                options = CreateOptions(provider, threads);
                var session = new InferenceSession(modelPath, options);
                logger.LogInformation(
                    "Loaded {Model} on {ExecutionProvider}{Device}",
                    fileName,
                    provider,
                    provider == ExecutionProvider.DirectMl ? $" (adapter {DescribeGpu()})" : string.Empty);
                return new OnnxSession(session, options, provider);
            }
            catch (Exception ex) when (index < attempts.Count - 1 && ex is not OperationCanceledException)
            {
                options?.Dispose();
                logger.LogWarning(ex, "Couldn't load {Model} on {ExecutionProvider}, trying {Next}", fileName, provider, attempts[index + 1]);
            }
            catch
            {
                options?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Records that a model file failed a run on a GPU provider (its first run, or after the device was lost), so later sessions of that file use the CPU
    /// until the app restarts.
    /// </summary>
    /// <param name="modelPath">Full path of the <c>.onnx</c> file.</param>
    /// <param name="provider">The provider it failed on.</param>
    /// <param name="error">The failure.</param>
    public void ReportRunFailure(string modelPath, ExecutionProvider provider, Exception error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(error);
        if (provider == ExecutionProvider.Cpu)
        {
            return;
        }

        failedRuns[(modelPath, provider)] = error.Message;
        logger.LogWarning(error, "{Model} failed a run on {ExecutionProvider}, switching it to the CPU", Path.GetFileName(modelPath), provider);
    }

    private static void AppendToOptions(SessionOptions options, ExecutionProvider provider, int deviceId)
    {
        switch (provider)
        {
            case ExecutionProvider.DirectMl:
                // DirectML doesn't support memory patterns or parallel execution.
                options.EnableMemoryPattern = false;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_DML(deviceId);
                break;
            case ExecutionProvider.Cuda:
                options.AppendExecutionProvider_CUDA(0);
                break;
            case ExecutionProvider.Nnapi:
                options.AppendExecutionProvider_Nnapi();
                break;
        }
    }

    private string DescribeGpu() => hardware?.PreferredGpu is { } gpu ? $"{gpu.AdapterIndex}, {gpu.Name}" : "0";

    private SessionOptions CreateOptions(ExecutionProvider provider, int threads)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (threads > 0)
        {
            options.IntraOpNumThreads = threads;
        }

        try
        {
            if (provider != ExecutionProvider.Cpu)
            {
                // DirectML takes the DXGI adapter index; CUDA numbers its devices differently, so it keeps device 0.
                AppendExecutionProvider(options, provider, provider == ExecutionProvider.DirectMl ? hardware?.PreferredGpu?.AdapterIndex ?? 0 : 0);
            }
        }
        catch
        {
            options.Dispose();
            throw;
        }

        return options;
    }
}
