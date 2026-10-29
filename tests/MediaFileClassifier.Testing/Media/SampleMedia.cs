namespace MediaFileClassifier.Testing.Media;

/// <summary>
/// Paths to the edge-case sample media corpus in <c>tests/SampleMedia</c> (E01-S11).
/// The files are described in <c>tests/SampleMedia/README.md</c>.
/// </summary>
public static class SampleMedia
{
    /// <summary>
    /// Gets the folder the corpus is copied to in the test output.
    /// </summary>
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "SampleMedia");

    /// <summary>
    /// Gets a 640×480 JPEG with EXIF: camera make and model, dates, Orientation 6 and a GPS position.
    /// </summary>
    public static string JpegWithExif => Combine("jpeg-exif.jpg");

    /// <summary>
    /// Gets a 256×256 PNG with an alpha channel.
    /// </summary>
    public static string PngWithAlpha => Combine("png-alpha.png");

    /// <summary>
    /// Gets a 320×240 lossy WebP.
    /// </summary>
    public static string WebP => Combine("webp.webp");

    /// <summary>
    /// Gets a single-frame GIF.
    /// </summary>
    public static string StaticGif => Combine("gif-static.gif");

    /// <summary>
    /// Gets a looping GIF with 8 frames.
    /// </summary>
    public static string AnimatedGif => Combine("gif-animated.gif");

    /// <summary>
    /// Gets a 2-second 320×240 MP4 with H.264 video and an AAC tone.
    /// </summary>
    public static string ShortVideo => Combine("video-short.mp4");

    /// <summary>
    /// Gets a PNG with the visible text "Invoice 2026-10-29" and "Счёт на оплату", for OCR.
    /// </summary>
    public static string ImageWithText => Combine("ocr-text.png");

    /// <summary>
    /// Gets a truncated JPEG: a valid header followed by junk, with no end-of-image marker.
    /// </summary>
    public static string CorruptJpeg => Combine("corrupt.jpg");

    /// <summary>
    /// Gets an empty file with a <c>.png</c> extension.
    /// </summary>
    public static string ZeroByteFile => Combine("zero-byte.png");

    /// <summary>
    /// Gets a valid JPEG with a Cyrillic file name that contains spaces.
    /// </summary>
    public static string NonAsciiName => Combine("фото с пробелом.jpg");

    private static string Combine(string fileName) => Path.Combine(Directory, fileName);
}
