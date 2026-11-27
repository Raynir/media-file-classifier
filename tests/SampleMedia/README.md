# Sample media corpus

A small set of edge-case files for tests (E01-S11). Tests reach them through `SampleMedia` in `MediaFileClassifier.Testing`, never by hard-coded paths. Every test project that references `MediaFileClassifier.Testing` gets them in `SampleMedia\` next to its binaries.

All files are synthetic, so they hold no personal data. The larger labelled corpus for search quality is E01-S10.

| File | `SampleMedia` property | Contents |
|------|------------------------|----------|
| `jpeg-exif.jpg` | `JpegWithExif` | 640×480 JPEG. EXIF: Make `MFC Test Camera Co.`, Model `Sample 1`, DateTimeOriginal `2026:10:29 12:34:56`, Orientation 6 (rotate 90° clockwise), GPS 55°58'48" S 67°17'21" W (Cape Horn; southern and western hemisphere refs). |
| `jpeg-icc-offset.jpg` | `JpegWithIccAndOffset` | 200×100 JPEG with an embedded sRGB ICC profile ("sRGB built-in"). EXIF: Make `MFC Test Camera Co.`, Model `Sample 2`, LensModel `MFC Lens 35mm`, DateTimeOriginal `2026:06:15 08:30:00`, OffsetTimeOriginal `+03:00`, SubSecTimeOriginal `25`, ColorSpace 1 (sRGB). |
| `png-alpha.png` | `PngWithAlpha` | 256×256 RGBA PNG, transparent outside a circle. |
| `webp.webp` | `WebP` | 320×240 lossy WebP. |
| `gif-static.gif` | `StaticGif` | 96×96 single-frame GIF (GIF87a). |
| `gif-animated.gif` | `AnimatedGif` | 96×96 GIF, 8 frames of 100 ms, looping. |
| `gif-blank-first-frame.gif` | `GifWithBlankFirstFrame` | 96×96 GIF, 6 frames of 100 ms, looping; the first frame is plain white, the others like `gif-animated.gif`. |
| `webp-animated.webp` | `AnimatedWebP` | 96×96 animated WebP, 6 frames of 120 ms, looping. |
| `video-short.mp4` | `ShortVideo` | 2 s, 320×240, 15 fps H.264, with a 440 Hz AAC tone. |
| `video-hevc.mp4` | `HevcVideo` | 1 s, 160×120, 10 fps HEVC (`hvc1`), no audio. |
| `video-av1.mp4` | `Av1Video` | 1 s, 160×120, 10 fps AV1 (`av01`), no audio. |
| `video-portrait.mp4` | `PortraitVideo` | 1 s, 160×90 H.264 stored landscape with a display matrix rotation of −90° (shown portrait, 90×160, like a phone held upright); container `creation_time` `2026-07-01T10:20:30Z`. |
| `video-black-intro.mp4` | `BlackIntroVideo` | 4 s, 160×120, 10 fps H.264, no audio: 1.5 s of black, then a test pattern. The frames at 10% and 25% of the duration are black. |
| `video-static-camera.mp4` | `StaticCameraVideo` | 80 s, 160×120, 2 fps H.264, no audio: one photo-like scene with light per-frame noise and a small dark dot crossing it, like a tripod shot. Frame sampling keeps few of its 16 positions. |
| `video-action.mp4` | `ActionVideo` | 80 s, 160×120, 2 fps H.264, no audio: cuts to a new, unrelated scene of coloured rectangles every 5 s, so all 16 sampled positions differ. |
| `gif-variable-delays.gif` | `GifWithVariableDelays` | 96×96 GIF, 13 frames over 4 s: frames 0 and 1 last 1 s, frames 2 to 11 100 ms, frame 12 1 s. Sampling 4 frames evenly in time picks frames 0, 1, 7 and 12. |
| `gif-three-frames.gif` | `GifWithThreeFrames` | 96×96 GIF, 3 different frames of 200 ms, looping: fewer frames than the minimum keyframe count. |
| `audio-only.mp4` | `AudioOnlyMp4` | 1 s 440 Hz AAC tone in an MP4 container, with no video track. |
| `ocr-text.png` | `ImageWithText` | Black text on white: "Invoice 2026-10-29" and "Счёт на оплату". |
| `corrupt.jpg` | `CorruptJpeg` | 500 bytes: the start of a valid JPEG, then junk, with no end-of-image marker. |
| `zero-byte.png` | `ZeroByteFile` | Empty file with an image extension. |
| `фото с пробелом.jpg` | `NonAsciiName` | Valid 160×120 JPEG with a Cyrillic name that contains spaces. |

## Robustness test set

`robustness/` checks file matching (E01-S06-T06); `SampleMedia.RobustnessDirectory` points to it. There are four synthetic photo-like originals, `photo-a.jpg` to `photo-d.jpg` (640×480, 1/f noise with shapes). Each has these copies:

| Copy | Expected match |
|------|----------------|
| `moved/deeper/renamed-<name>.jpg`: byte-identical, renamed and moved | content hash |
| `<name>-q50.jpg`, `<name>-q20.jpg`: re-encoded JPEG | perceptual |
| `<name>-half.jpg`: resized to 50 % | perceptual |
| `<name>-thumb.jpg`: a 160×120 thumbnail | perceptual |
| `<name>.webp`: lossy WebP | perceptual |
| `<name>.png`: PNG | perceptual |
| `<name>-screenshot.png`: the image at 75 % in an app window on a desktop | none |

Two more copies are `photo-a-exif-rotated.jpg` (pixels stored rotated, with EXIF orientation 6), and `gif-animated.webp` (the animated GIF sample as an animated WebP). Both are expected to match perceptually.

`expected.json` lists every copy with its original and the expected match. On this set, perceptual copies are 0 to 2 bits from their original, and screenshots 32 to 34. That's what the threshold of 8 bits in `PerceptualHash.SamePictureThreshold` is based on.

## Visual near-duplicate test set

`near-duplicates/` checks visual near-duplicate detection (E12-S02); `SampleMedia.NearDuplicatesDirectory` points to it. It holds no photos: `features.json` lists, for each item, what matching reads of it, captured with the real SigLIP 2 model, so CI compares real similarities without the model:

- the perceptual hash (stills) or keyframe hashes and duration (videos), and the whole-item vector as little-endian half-precision base64 (D067);
- which items are exact copies (`content`), and the visual group expected at the strict and the loose sensitivity.

The stills are evaluation corpus photos (E01-S10) and copies made of them in memory: re-encoded at quality 30, half size, a 320-pixel thumbnail, a brightness and contrast edit, 4 % and 8 % crops, and four burst-like shots (an 88 % window drifting across one photo). Different photos of the same subject (St Basil's Cathedral, squirrels, tulips) must stay apart. The videos are `video-short.mp4` with `near-duplicates/video-short-reencoded.mp4`, which must group, and `video-action.mp4` with `video-static-camera.mp4`, which must not; their vectors are the mean of five frames. The animated images are `gif-animated.gif` with its WebP conversion `robustness/gif-animated.webp`, which must group, and `webp-animated.webp`, another synthetic animation that the model finds alike (cosine 0.96 to 0.98), so only the loose sensitivity groups it with them; their frames are decoded with SkiaSharp at the same five positions.

To capture it again (needs the model, see `tools/embedding-reference/README.md`, the corpus folder, and ffmpeg):

```powershell
$env:MFC_SIGLIP2_DIR = "$env:LOCALAPPDATA\MediaFileClassifier\models\siglip2-base-patch16-256\1.0\standard"
$env:MFC_NEAR_DUPLICATE_CORPUS = "<corpus folder>"
$env:MFC_WRITE_NEAR_DUPLICATE_FEATURES = "$PWD\tests\SampleMedia\near-duplicates\features.json"
dotnet test --project tests/MediaFileClassifier.Infrastructure.Tests -- --filter-method "*Capture_WritesFeatures"
```

The re-encoded video comes from `python tools/sample-media/generate_near_duplicate_set.py tests/SampleMedia/near-duplicates` (ffmpeg).

## Regenerating

The files come from `tools/sample-media/generate_sample_media.py`, which needs Python 3, Pillow and ffmpeg on `PATH`. The output is deterministic on the same tool versions.

```powershell
python tools/sample-media/generate_sample_media.py tests/SampleMedia
```

To add or change some files without touching the others, name them after the folder, e.g. `... tests/SampleMedia video-av1.mp4`.

When you add or change a file, update this table and `SampleMedia.cs`.

The robustness test set needs only Python 3 and Pillow, and reads `gif-animated.gif`:

```powershell
python tools/sample-media/generate_robustness_set.py tests/SampleMedia/robustness
```
