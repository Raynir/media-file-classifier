# Media identity and hashing

How the app recognises a file by its content, independent of its path (E01-S06). One mechanism serves moved and renamed files (E04), duplicates (E12), metadata import (E07, D076) and phone libraries (E20, D004).

The interfaces are in `Infrastructure.Abstractions/Hashing` and `Infrastructure.Abstractions/Matching`; the implementations are in `Infrastructure/Hashing` and `Infrastructure/Matching`. All of it is shared code, so Android computes the same hashes as the desktop.

## The three hashes

`MediaItem.Hashes` (`FileHashes`) holds them; each is `null` until it's computed.

| Hash | What | When | Cost |
|------|------|------|------|
| **Fingerprint** | XxHash128 of the first, middle and last 64 KB, or of the whole file when it's at most 192 KB. Only meaningful with the file size. | During discovery | Three reads, about 1 ms even for a 4 GB video |
| **Content hash** | XxHash128 of the whole file, streamed in 1 MB blocks | Low-priority background job (E05) | Disk-bound; 3.5 GB/s from the page cache |
| **Perceptual hash** | 64-bit DCT pHash of the displayed image | With the content hash, for images and animated images only | Decoding dominates; JPEGs are decoded at reduced scale |

- **Algorithms:** XxHash128 from `System.IO.Hashing`, as the backlog specifies. Changing `XxHashFileHasher.DefaultChunkSize` changes every fingerprint.
- **Non-seekable streams** (some Android content URIs) are read through to the middle and last chunks, and give the same fingerprint.
- **Limitation:** a change that falls between the fingerprint's chunks and keeps the size keeps the fingerprint. The content hash catches it, which is why matching never trusts a fingerprint over two known content hashes that differ.
- **Storage:** 128-bit hashes are 32 hex digits, the perceptual hash is a signed INTEGER with the same bits (D071). The content hash and fingerprint columns are indexed.
- **Entry points:** `IFileHasher` works on streams. `IMediaHasher` reads a file through `IMediaFileSource`: `ComputeFingerprintAsync` for discovery, and `ComputeMissingAsync` for the background job, which keeps the hashes already known. `IMediaItemRepository.GetWithoutContentHashAsync` lists the items that still need it.

### Perceptual hash

`SkiaPerceptualHasher` decodes with SkiaSharp, the version Avalonia ships, so it needs no other native library and works on Android.

1. Decode the first frame. Animated GIFs and WebPs are hashed by their first frame. JPEGs are decoded at reduced scale, to at least 128 px on the shorter side.
2. Composite over white, and convert to luma (Rec. 601).
3. Box-filter to 32×32. The resampling is done in managed code, so the hash doesn't depend on Skia's filters.
4. Turn the grid to the displayed orientation (EXIF orientation).
5. Take the 2-D DCT, and keep the 8×8 lowest frequencies without the DC row and column.
6. Each coefficient above their median is a 1 bit, row-major from the most significant bit.

Files Skia can't decode, e.g. truncated JPEGs, get no perceptual hash. HEIC decodes on Android only.

**Threshold:** two images are the same picture when their hashes are at most `PerceptualHash.SamePictureThreshold` = 8 bits apart. On the robustness test set, copies are 0 to 2 bits from their original, and unrelated images at least 10 (median 34). E12 can add a looser setting for visual duplicates.

**Near-match search:** `PerceptualHashIndex<TKey>` is a linear scan with `PopCount`. 100,000 hashes take well under a millisecond, against the 100 ms budget.

## File matching

`IFileMatcherFactory.CreateAsync(items)` gives an `IFileMatcher` for one pass, e.g. a rescan or an import. It reads the items' perceptual hashes and file names once; the exact tiers query the database each time. `MatchAsync(FileFacts)` tries the tiers in order, and the first that finds anything wins:

| Tier | `FileMatchType` | Matches when | `MatchConfidence` |
|------|-----------------|--------------|-------------------|
| 1 | `ContentHash` | Same content hash | `Certain` |
| 2 | `Fingerprint` | Same fingerprint and size, and the content hashes don't differ | `High` |
| 3 | `Perceptual` | Images and animated images, at most 8 bits apart | `Medium` |
| 4 | `FileNameAndMetadata` | Same file name (ignoring case) and media type, and the same size or the same modification time to the second | `Low` |

- **Missing hashes** skip their tier, so a file seen during discovery, with only a fingerprint, starts at tier 2.
- **Several items** match when the library holds duplicates. The result lists them all, best first; a perceptual match gives each one's distance.
- **D076:** a package merge applies `Certain` and `High` matches automatically, and asks the user to confirm `Medium` and `Low` ones.

## Robustness test set

`tests/SampleMedia/robustness` (E01-S06-T06) has four photo-like originals and copies of each: renamed and moved, re-encoded at quality 50 and 20, resized to 50 % and to a thumbnail, converted to WebP and PNG, and a screenshot of the image in an app window. There's also an EXIF-rotated copy and an animated WebP of the animated GIF sample. `expected.json` gives each copy's expected match, and `RobustnessSetTests` checks them all. See `tests/SampleMedia/README.md` to regenerate it.

## Measuring

The fingerprint benchmark writes a 4 GiB file to the temp folder and is skipped unless asked for:

```powershell
dotnet test tests/MediaFileClassifier.Infrastructure.Tests -c Release -- --filter-method "*Benchmark_MultiGigabyteFile" --explicit only
```

On the development machine: fingerprint 21 ms on the first call (with JIT), then about 1 ms; content hash 1.2 s (3.5 GB/s) with the file in the page cache. A cold read is bound by the disk.
