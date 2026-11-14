namespace MediaFileClassifier.Infrastructure.Abstractions.Hardware;

/// <summary>
/// ONNX Runtime execution providers the app can choose between.
/// </summary>
public enum ExecutionProvider
{
    /// <summary>
    /// The default CPU provider. Always available.
    /// </summary>
    Cpu,

    /// <summary>
    /// DirectML on a DirectX 12 graphics adapter (Windows).
    /// </summary>
    DirectMl,

    /// <summary>
    /// CUDA on an NVIDIA GPU. Needs the GPU build of ONNX Runtime in the head.
    /// </summary>
    Cuda,

    /// <summary>
    /// The Android Neural Networks API.
    /// </summary>
    Nnapi,
}
