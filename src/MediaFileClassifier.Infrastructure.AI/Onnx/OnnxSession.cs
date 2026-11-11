using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using Microsoft.ML.OnnxRuntime;

namespace MediaFileClassifier.Infrastructure.AI.Onnx;

/// <summary>
/// A loaded ONNX model, with the execution provider it actually runs on. <see cref="InferenceSession.Run(IReadOnlyCollection{NamedOnnxValue})"/>
/// is thread-safe, so one session serves every caller.
/// </summary>
public sealed class OnnxSession : IDisposable
{
    private readonly SessionOptions options;

    internal OnnxSession(InferenceSession session, SessionOptions options, ExecutionProvider executionProvider)
    {
        Session = session;
        this.options = options;
        ExecutionProvider = executionProvider;
    }

    public InferenceSession Session { get; }

    public ExecutionProvider ExecutionProvider { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a run has succeeded on this session. Until then, a failed run on a GPU
    /// provider moves the model to the CPU (E06-S08).
    /// </summary>
    internal bool HasRun { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        Session.Dispose();
        options.Dispose();
    }
}
