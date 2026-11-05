namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// How a provider runs its model (D005).
/// </summary>
public enum AiProviderKind
{
    /// <summary>
    /// An in-process model run by ONNX Runtime.
    /// </summary>
    Onnx,

    /// <summary>
    /// An OpenAI-compatible HTTP endpoint, such as LM Studio, Ollama or a cloud API.
    /// </summary>
    OpenAiCompatible,

    /// <summary>
    /// A wrapper around a native library, such as Whisper.net or Tesseract.
    /// </summary>
    NativeLibrary,

    /// <summary>
    /// A deterministic stand-in used by tests.
    /// </summary>
    Fake,
}
