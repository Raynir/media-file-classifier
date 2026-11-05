using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// Settings of the SigLIP 2 provider.
/// </summary>
public sealed record Siglip2Options
{
    /// <summary>
    /// Gets the most images per ONNX call. Larger lists are split. On the CPU, batches don't speed up the model (MFC-85
    /// measured 14.7 images/s at 1 and 14.3 at 4); 4 keeps the path in use for GPUs.
    /// </summary>
    public int ImageBatchSize { get; init; } = 4;

    /// <summary>
    /// Gets the execution provider to try first. The session factory falls back to the CPU.
    /// </summary>
    public ExecutionProvider ExecutionProvider { get; init; } = ExecutionProvider.Cpu;
}
