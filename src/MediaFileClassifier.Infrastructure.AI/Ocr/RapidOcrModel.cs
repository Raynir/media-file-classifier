using MediaFileClassifier.Infrastructure.Abstractions.Hardware;

namespace MediaFileClassifier.Infrastructure.AI.Ocr;

/// <summary>
/// Names of the in-process OCR model (D347): PaddleOCR's PP-OCRv5 mobile text detector and its East Slavic
/// recogniser (Russian, Ukrainian, Belarusian and basic Latin), as ONNX files packaged by RapidOCR (Apache-2.0).
/// </summary>
public static class RapidOcrModel
{
    /// <summary>
    /// The model ID in the catalogue and the model store.
    /// </summary>
    public const string ModelId = "pp-ocrv5-mobile-eslav";

    /// <summary>
    /// The model version in the catalogue and the model store.
    /// </summary>
    public const string Version = "1.0";

    /// <summary>
    /// The provider ID.
    /// </summary>
    public const string ProviderId = "onnx:pp-ocrv5-mobile-eslav";

    /// <summary>
    /// The role of the text detector file.
    /// </summary>
    public const string DetectorRole = "detector";

    /// <summary>
    /// The role of the text recogniser file.
    /// </summary>
    public const string RecognizerRole = "recognizer";

    /// <summary>
    /// The version of the pre- and post-processing, stored with each run.
    /// </summary>
    public const string ProcessingVersion = "ppocr-db736-ctc48-1";
}

/// <summary>
/// Settings of the in-process OCR provider, with RapidOCR's defaults.
/// </summary>
public sealed record RapidOcrOptions
{
    /// <summary>
    /// Gets the execution provider to try first, or <c>null</c> to choose it from the hardware. The session factory
    /// falls back to the CPU.
    /// </summary>
    public ExecutionProvider? ExecutionProvider { get; init; }

    /// <summary>
    /// Gets the short side the detector sees at least; a smaller picture is enlarged to it.
    /// </summary>
    public int DetectionMinSide { get; init; } = 736;

    /// <summary>
    /// Gets the longest side the detector sees at most. RapidOCR caps it at 2000; 960 read the corpus better (CER 0.066
    /// against 0.076), in half the time, with fewer stray characters on pictures without text (MFC-331).
    /// </summary>
    public int DetectionMaxSide { get; init; } = 960;

    /// <summary>
    /// Gets the least confidence a recognised line needs to be kept (RapidOCR's <c>text_score</c>).
    /// </summary>
    public float MinTextConfidence { get; init; } = 0.5f;

    /// <summary>
    /// Gets the fewest letters and digits a line needs to be kept; a lone mark is a misread (MFC-334).
    /// </summary>
    public int MinTextCharacters { get; init; } = 2;

    /// <summary>
    /// Gets the most letters and digits a line can have to count as short (MFC-334).
    /// </summary>
    public int ShortTextCharacters { get; init; } = 3;

    /// <summary>
    /// Gets the least confidence a short line needs to be kept (MFC-334).
    /// </summary>
    public float MinShortTextConfidence { get; init; } = 0.8f;

    /// <summary>
    /// Gets how many lines the recogniser reads per call.
    /// </summary>
    public int RecognitionBatchSize { get; init; } = 6;
}
