namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// The SigLIP 2 base-patch16-256 embedding model (D092, D093): how it's stored in the model store and its fixed
/// input and output sizes.
/// </summary>
public static class Siglip2Model
{
    /// <summary>
    /// The model's ID in the model store and the catalogue (E06-S12).
    /// </summary>
    public const string ModelId = "siglip2-base-patch16-256";

    /// <summary>
    /// The installed version the provider looks for.
    /// </summary>
    public const string Version = "1.0";

    /// <summary>
    /// The provider's ID in the registry.
    /// </summary>
    public const string ProviderId = "onnx:siglip2-base-patch16-256";

    /// <summary>
    /// File role of the image tower (<c>vision_model.onnx</c>).
    /// </summary>
    public const string VisionRole = "vision";

    /// <summary>
    /// File role of the fp32 text tower (<c>text_model.onnx</c>).
    /// </summary>
    public const string TextRole = "text";

    /// <summary>
    /// File role of the Gemma SentencePiece model (<c>tokenizer.model</c>).
    /// </summary>
    public const string TokenizerRole = "tokenizer";

    /// <summary>
    /// Vector length.
    /// </summary>
    public const int Dimension = 768;

    /// <summary>
    /// Width and height of the image input.
    /// </summary>
    public const int ImageSize = 256;

    /// <summary>
    /// Token count of the text input, padding included.
    /// </summary>
    public const int SequenceLength = 64;

    /// <summary>
    /// Version of the input preparation, stored as the output's prompt version so a change to it is traceable.
    /// </summary>
    public const string PreprocessingVersion = "siglip2-pil-bilinear-256/gemma-lower-64";
}
