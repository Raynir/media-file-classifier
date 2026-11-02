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
| `audio-only.mp4` | `AudioOnlyMp4` | 1 s 440 Hz AAC tone in an MP4 container, with no video track. |
| `ocr-text.png` | `ImageWithText` | Black text on white: "Invoice 2026-10-29" and "Счёт на оплату". |
| `corrupt.jpg` | `CorruptJpeg` | 500 bytes: the start of a valid JPEG, then junk, with no end-of-image marker. |
| `zero-byte.png` | `ZeroByteFile` | Empty file with an image extension. |
| `фото с пробелом.jpg` | `NonAsciiName` | Valid 160×120 JPEG with a Cyrillic name that contains spaces. |

## Regenerating

The files come from `tools/sample-media/generate_sample_media.py`, which needs Python 3, Pillow and ffmpeg on `PATH`. The output is deterministic on the same tool versions.

```powershell
python tools/sample-media/generate_sample_media.py tests/SampleMedia
```

To add or change some files without touching the others, name them after the folder, e.g. `... tests/SampleMedia video-av1.mp4`.

When you add or change a file, update this table and `SampleMedia.cs`.
