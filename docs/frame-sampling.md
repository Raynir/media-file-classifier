# Frame sampling

How the app picks the keyframes of videos and animated images for analysis (E06-S02, D013). Animated GIF and WebP files are treated as short silent videos. The code is `IFrameSampler` in `Infrastructure.Abstractions/Frames` and `FrameSampler` in `Infrastructure.Media/Frames`.

## Positions

- **Count:** one position per 5 seconds of the duration, rounded down, at least 4 and at most 16. A 20-second clip gets 4, a 50-second clip 10, and anything from 80 seconds up 16.
- **Where:** the midpoints of equal slices of the duration, so a fade at the very start or end isn't hit. A 20-second clip is read at 2.5, 7.5, 12.5 and 17.5 seconds.
- **Unknown duration:** only the first frame.

`FrameSamplingOptions` holds these values. The analysis profile will map onto it when it exists.

## Dropping frames

Frames are checked in time order:

1. **Blank frames** are dropped: mean luma below 16 of 255, or one flat colour, the same test the video thumbnail uses (D125). If every frame is blank, the brightest one is kept, so the item is still analysed.
2. **Near duplicates** are dropped: a frame whose perceptual hash is at most 8 bits from the previous *kept* frame (`PerceptualHash.SamePictureThreshold`). Comparing with the last kept frame, not the last one read, means a slow drift still produces a new keyframe once it adds up.

The kept frames are indexed from 0. On the sample media, the 80-second static-camera video keeps 1 to 3 of its 16 positions, and the action video, which cuts to a new scene every 5 seconds, keeps all 16.

## Reading frames

- **Video:** FFmpeg reads each position with its own exact seek (`-ss` before the input), turns the frame upright by the display rotation, and scales it down. Without FFmpeg, a video gets no frames and the warning `FrameSamplingFfmpegUnavailable`.
- **Failures:** a position FFmpeg can't read is skipped. If no frame can be read, the result has no frames and the warning `FrameSamplingFailed` with the error. Only a still image, or invalid options, throws.

Measured on an i7-14700HX for 16 frames:

| Video | One seek per frame (chosen) | One pass, all cores | One pass, one thread | Keyframes only |
|-------|------|------|------|------|
| 1080p, 120 s | 2.4 s | 1.3 s | 9.3 s | 0.4 s |
| 4K, 60 s | 3.8 s | 1.7 s | 10.4 s | 0.5 s |

A seek per frame costs the same for a two-hour film, while a single pass grows with the duration. Reading only the codec's keyframes is fastest, but snaps positions to I-frames, which can be seconds apart on phone video.

## Output

Each `SampledFrame` has its index, timestamp (for animated images, the sum of the preceding frame delays), frame number (animated images only), perceptual hash, and pixels:

- RGBA, 8 bits per channel, row-major, no padding, upright.
- Transparent areas are composited over white, as the perceptual hash sees them, so alpha is always 255.
- At most 1024 pixels on the longest edge, never upscaled: enough for SigLIP 2's 256 × 256 input (D092) and for OCR of on-screen text. 16 frames of 16:9 video take about 38 MB.

`SampledFrame.ToKeyframe` makes the `Keyframe` to store with `IKeyframeRepository.ReplaceAsync`. Replacing an item's keyframes deletes the old ones with their per-frame embeddings.
