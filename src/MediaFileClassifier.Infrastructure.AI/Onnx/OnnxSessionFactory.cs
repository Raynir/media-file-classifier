using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.AI.Onnx;

/// <summary>
/// Creates ONNX Runtime inference sessions, trying the preferred execution provider first and falling back to the CPU
/// (D010). Which execution providers exist depends on the native runtime the head ships: the CPU package on Desktop and
/// Android for now, and DirectML or CUDA once E06-S08 chooses a GPU package.
/// </summary>
/// <param name="logger">Logger.</param>
public sealed class OnnxSessionFactory(ILogger<OnnxSessionFactory>? logger = null)
{
    private static readonly IReadOnlyDictionary<ExecutionProvider, string> RuntimeNames = new Dictionary<ExecutionProvider, string>
    {
        [ExecutionProvider.Cpu] = "CPUExecutionProvider",
        [ExecutionProvider.DirectMl] = "DmlExecutionProvider",
        [ExecutionProvider.Cuda] = "CUDAExecutionProvider",
        [ExecutionProvider.Nnapi] = "NnapiExecutionProvider",
    };

    private readonly ILogger logger = logger ?? NullLogger<OnnxSessionFactory>.Instance;

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
    /// <exception cref="OnnxRuntimeException">The model doesn't load on any execution provider.</exception>
    public OnnxSession Create(string modelPath, ExecutionProvider preferred, int threads = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The model file doesn't exist.", modelPath);
        }

        var attempts = GetAttemptOrder(preferred, GetAvailableProviders());
        for (var index = 0; ; index++)
        {
            var provider = attempts[index];
            var options = CreateOptions(provider, threads);
            try
            {
                var session = new InferenceSession(modelPath, options);
                logger.LogInformation("Loaded {Model} on {ExecutionProvider}", Path.GetFileName(modelPath), provider);
                return new OnnxSession(session, options, provider);
            }
            catch (OnnxRuntimeException ex) when (index < attempts.Count - 1)
            {
                options.Dispose();
                logger.LogWarning(ex, "Couldn't load {Model} on {ExecutionProvider}, falling back to the CPU", Path.GetFileName(modelPath), provider);
            }
            catch
            {
                options.Dispose();
                throw;
            }
        }
    }

    private static SessionOptions CreateOptions(ExecutionProvider provider, int threads)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (threads > 0)
        {
            options.IntraOpNumThreads = threads;
        }

        try
        {
            switch (provider)
            {
                case ExecutionProvider.DirectMl:
                    // DirectML doesn't support memory patterns or parallel execution.
                    options.EnableMemoryPattern = false;
                    options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                    options.AppendExecutionProvider_DML(0);
                    break;
                case ExecutionProvider.Cuda:
                    options.AppendExecutionProvider_CUDA(0);
                    break;
                case ExecutionProvider.Nnapi:
                    options.AppendExecutionProvider_Nnapi();
                    break;
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
