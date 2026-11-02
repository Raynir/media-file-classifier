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
| `webp-animated.webp` | `AnimatedWebP` | 96×96 animated WebP, 6 frames of 120 ms, looping. |
| `video-short.mp4` | `ShortVideo` | 2 s, 320×240, 15 fps H.264, with a 440 Hz AAC tone. |
| `video-hevc.mp4` | `HevcVideo` | 1 s, 160×120, 10 fps HEVC (`hvc1`), no audio. |
| `video-av1.mp4` | `Av1Video` | 1 s, 160×120, 10 fps AV1 (`av01`), no audio. |
| `video-portrait.mp4` | `PortraitVideo` | 1 s, 160×90 H.264 stored landscape with a display matrix rotation of −90° (shown portrait, 90×160, like a phone held upright); container `creation_time` `2026-07-01T10:20:30Z`. |
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
