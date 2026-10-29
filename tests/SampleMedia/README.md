# Sample media corpus

A small set of edge-case files for tests (E01-S11). Tests reach them through `SampleMedia` in `MediaFileClassifier.Testing`, never by hard-coded paths. Every test project that references `MediaFileClassifier.Testing` gets them in `SampleMedia\` next to its binaries.

All files are synthetic, so they hold no personal data. The larger labelled corpus for search quality is E01-S10.

| File | `SampleMedia` property | Contents |
|------|------------------------|----------|
| `jpeg-exif.jpg` | `JpegWithExif` | 640×480 JPEG. EXIF: Make `MFC Test Camera Co.`, Model `Sample 1`, DateTimeOriginal `2026:10:29 12:34:56`, Orientation 6 (rotate 90° clockwise), GPS 55°58'48" S 67°17'21" W (Cape Horn; southern and western hemisphere refs). |
| `png-alpha.png` | `PngWithAlpha` | 256×256 RGBA PNG, transparent outside a circle. |
| `webp.webp` | `WebP` | 320×240 lossy WebP. |
| `gif-static.gif` | `StaticGif` | 96×96 single-frame GIF (GIF87a). |
| `gif-animated.gif` | `AnimatedGif` | 96×96 GIF, 8 frames of 100 ms, looping. |
| `video-short.mp4` | `ShortVideo` | 2 s, 320×240, 15 fps H.264, with a 440 Hz AAC tone. |
| `ocr-text.png` | `ImageWithText` | Black text on white: "Invoice 2026-10-29" and "Счёт на оплату". |
| `corrupt.jpg` | `CorruptJpeg` | 500 bytes: the start of a valid JPEG, then junk, with no end-of-image marker. |
| `zero-byte.png` | `ZeroByteFile` | Empty file with an image extension. |
| `фото с пробелом.jpg` | `NonAsciiName` | Valid 160×120 JPEG with a Cyrillic name that contains spaces. |

## Regenerating

The files come from `tools/sample-media/generate_sample_media.py`, which needs Python 3, Pillow and ffmpeg on `PATH`. The output is deterministic on the same tool versions.

```powershell
python tools/sample-media/generate_sample_media.py tests/SampleMedia
```

When you add or change a file, update this table and `SampleMedia.cs`.
