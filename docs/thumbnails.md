# Thumbnails

How the app makes and stores thumbnails (E03-S04). The thumbnail generators live in `Infrastructure.Media` (D097), and each format handler owns one (D098).

## Format and sizes

- **Two sizes:** 256 and 512 pixels on the longest edge, so a grid cell stays sharp at every size on high-DPI screens. Smaller images are never upscaled.
- **WebP at quality 80**, encoded with SkiaSharp. Avalonia already ships SkiaSharp on Windows and Android, so this adds no native library. WebP keeps transparency.
- **Resampling:** large reductions halve with a 2×2 box filter, then take the last step with a Mitchell cubic, so they don't alias. JPEGs decode at a reduced size (1/2, 1/4 or 1/8) when that still gives at least 512 pixels.

Measured on a desktop CPU, one thread, both sizes per item:

| Source | Time | 256 | 512 |
|--------|------|-----|-----|
| 12 MP JPEG | 46 ms | 5.5 KB | 14.4 KB |
| 24 MP JPEG | 37 ms | 1.8 KB | 5.0 KB |
| 640×480 photo-like JPEG (`jpeg-exif.jpg`) | 33 ms | 9.7 KB | 50.7 KB |
| 320×240 WebP | 14 ms | 13.3 KB | 20.5 KB |
| 1080p video, 30 s | 159 ms | 2.8 KB | 6.8 KB |
| 4K video, 60 s | 401 ms | 2.4 KB | 6.5 KB |

Video time is mostly starting FFmpeg, once per position tried.

## Orientation

- **Photos:** the EXIF orientation from the item's metadata (E03-S03) turns the pixels upright before scaling. The decoder leaves the pixels as stored.
- **Videos:** FFmpeg applies the display rotation itself when it reads a frame (D106), so the thumbnailer doesn't rotate again.

## Representative frames

- **Animated GIF and WebP:** the first frame that isn't one flat colour within the first 10 frames, so an empty first frame doesn't become the thumbnail. If all ten are flat, the first frame.
- **Video:** the frame at 10% of the duration, past most intros. If it's blank (mean luma below 16 of 255, or one flat colour), the thumbnailer tries 25% and 50%, then the first frame. If every frame it tries is blank, it keeps the brightest. A video of unknown duration tries the first frame only.
- **Without FFmpeg** (D068), a video gets no thumbnail and the warning `ThumbnailFfmpegUnavailable`. That's never a failure: the item is registered and shows a placeholder. Regenerating after FFmpeg is installed makes the thumbnail.

## Cache

`IThumbnailCache` keeps the files at `<name>.mfc/thumbnails/<xx>/<item-id>-256.webp` and `-512.webp`, where `xx` is the last two hex digits of the item ID (D078, `LibraryLayout.GetThumbnailPath`). The files are the only state: there's no database column.

- **Lookup:** `Find` builds the path from the item ID and checks the file, so it takes constant time. A thumbnail written before the item's file last changed is stale, and `Find` returns `null`. New thumbnails are stamped with the current time or the file's modification time, whichever is later, so a file time in the future doesn't keep them stale.
- **Generation:** `GenerateAsync` runs the item's format handler's generator on the thread pool, at most half the cores at a time, until the job system (E05) schedules the work. It writes through a temporary file, so a reader never sees half a thumbnail, and replaces any old ones.
- **Failure:** a file that can't be decoded gives no thumbnail and the warning `ThumbnailFailed` with the error. The old thumbnails are deleted. The import pipeline (E03-S07) stores the warnings with the item's technical metadata warnings.
- **Invalidation:** `Invalidate` deletes an item's thumbnails, when its file changes (E02-S04-T03) or the item is removed. Regenerating (E02-S06-T02) is another `GenerateAsync`.

## Keyframe pictures

The preview's keyframe strip (E14-S13, D227) shows a picture for each keyframe of a video or animated image. Analysis writes them from the frames it has already decoded (`IKeyframeThumbnails.StoreAsync`, called from `ItemKeyframeSampler`, which samples and stores an item's keyframes for both `AnalysisInputs` and `ItemEmbeddingAnalyzer`; the Debug-only smoke runner seeds its own), as WebP at quality 80 with a longest edge of 192 pixels, next to the item's thumbnails: `thumbnails/<xx>/<item-id>-frame-<index>.webp` (`LibraryLayout.GetKeyframeThumbnailPath`). There is no database column. Keyframe IDs change each time an item is sampled again, so the file is named by item and index; a new analysis deletes the item's old pictures first. `IThumbnailCache.Invalidate` deletes them with the thumbnails. A file-system failure is logged and leaves the strip without pictures. Items analysed before this show the keyframes' times only until they're analysed again.

## Placeholders

`MediaThumbnail` in `Ui/Controls` is the thumbnail cell. It shows the thumbnail when there is one. Otherwise it shows a placeholder: the media type's icon (image, animated image or video) on a tile, with an error badge when generation failed. The placeholder is drawn rather than stored, so it follows the theme and stays sharp at any grid size, and the media browser never shows an empty cell. The look is provisional until the media browser design (E14).
