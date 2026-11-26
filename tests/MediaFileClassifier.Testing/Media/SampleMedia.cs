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
    /// Gets a 200×100 JPEG with an sRGB ICC profile, and EXIF with a lens, a time zone offset and subseconds.
    /// </summary>
    public static string JpegWithIccAndOffset => Combine("jpeg-icc-offset.jpg");

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
    /// Gets a 96×96 animated GIF whose first frame is plain white.
    /// </summary>
    public static string GifWithBlankFirstFrame => Combine("gif-blank-first-frame.gif");

    /// <summary>
    /// Gets a 4 s video whose first 1.5 s are black.
    /// </summary>
    public static string BlackIntroVideo => Combine("video-black-intro.mp4");

    /// <summary>
    /// Gets an 80 s video from a still camera: one scene with light noise and a small dot crossing it.
    /// </summary>
    public static string StaticCameraVideo => Combine("video-static-camera.mp4");

    /// <summary>
    /// Gets an 80 s video that cuts to an unrelated scene every 5 s.
    /// </summary>
    public static string ActionVideo => Combine("video-action.mp4");

    /// <summary>
    /// Gets a looping GIF with 13 frames over 4 s: frames 0, 1 and 12 last 1 s each, frames 2 to 11 100 ms each.
    /// </summary>
    public static string GifWithVariableDelays => Combine("gif-variable-delays.gif");

    /// <summary>
    /// Gets a looping GIF with 3 different frames of 200 ms.
    /// </summary>
    public static string GifWithThreeFrames => Combine("gif-three-frames.gif");

    /// <summary>
    /// Gets a looping animated WebP with 6 frames of 120 ms.
    /// </summary>
    public static string AnimatedWebP => Combine("webp-animated.webp");

    /// <summary>
    /// Gets a 2-second 320×240 MP4 with H.264 video and an AAC tone.
    /// </summary>
    public static string ShortVideo => Combine("video-short.mp4");

    /// <summary>
    /// Gets a 1-second 160×120 MP4 with HEVC video (<c>hvc1</c>) and no audio.
    /// </summary>
    public static string HevcVideo => Combine("video-hevc.mp4");

    /// <summary>
    /// Gets a 1-second 160×120 MP4 with AV1 video and no audio.
    /// </summary>
    public static string Av1Video => Combine("video-av1.mp4");

    /// <summary>
    /// Gets a 1-second MP4 stored 160×90 with a −90° display rotation, so it shows portrait, and a container
    /// creation time of 2026-07-01 10:20:30 UTC.
    /// </summary>
    public static string PortraitVideo => Combine("video-portrait.mp4");

    /// <summary>
    /// Gets an MP4 with an AAC audio track and no video track.
    /// </summary>
    public static string AudioOnlyMp4 => Combine("audio-only.mp4");

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

    /// <summary>
    /// Gets the folder of the media identity robustness test set (E01-S06-T06). Its <c>expected.json</c> lists every
    /// derived file with its original and the match tier expected for it.
    /// </summary>
    public static string RobustnessDirectory => Combine("robustness");

    /// <summary>
    /// Gets the folder of the visual near-duplicate test set (E12-S02). Its <c>expected.json</c> lists every file with
    /// the visual group expected at each sensitivity, and <c>vectors.json</c> holds the SigLIP 2 vectors and
    /// perceptual hashes of the files.
    /// </summary>
    public static string NearDuplicatesDirectory => Combine("near-duplicates");

    private static string Combine(string fileName) => Path.Combine(Directory, fileName);
}
