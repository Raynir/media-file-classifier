namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// The detected hardware, and the defaults chosen from it (E01-S07-T04).
/// </summary>
public interface IHardwareInfo
{
    /// <summary>
    /// Gets the detected hardware. Detection runs once, on first use.
    /// </summary>
    HardwareProfile Profile { get; }

    /// <summary>
    /// Gets the ONNX execution provider to try first. The AI layer (E06) uses it when it creates an inference
    /// session, and falls back to <see cref="ExecutionProvider.Cpu"/> if the provider fails to load.
    /// </summary>
    ExecutionProvider PreferredExecutionProvider { get; }

    /// <summary>
    /// Gets the default number of files processed in parallel.
    /// </summary>
    int DefaultParallelism { get; }
}
