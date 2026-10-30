"""Generates the clips the playback prototype measures (MFC-54, E14-SP01).

The clips are too large to commit, so they're generated on demand into a folder
outside the repository. The small committed corpus in tests/SampleMedia is played
as well; this script only adds what it lacks: H.265, AV1, 4K, longer clips with a
sparse keyframe interval, and an animated WebP.

Every video frame carries its frame number as a 16-bit binary code in a 32 px band
at the top (white block = 1, least significant bit on the left). The prototype reads
it back after a seek, so seek accuracy is measured in frames, without OCR.

Requirements: Python 3 and an ffmpeg build with libx264, libx265, libsvtav1 and
libwebp (the gyan.dev "full" build has them). The output is only for local testing,
so a GPL build is fine here (D068 is about what the app distributes).

Usage (from the repository root):
    python prototypes/MediaFileClassifier.Prototypes.Playback/generate_playback_media.py %TEMP%/mfc-playback-media
"""

import argparse
import subprocess
import sys
from pathlib import Path

FPS = 30
SECONDS = 20
# One keyframe every 4 s: seeking to a frame between keyframes needs decoding forward.
GOP = 4 * FPS

# The frame-code band: a 16x1 image whose pixel x is bit x of the frame number,
# scaled up without smoothing. Evaluating geq on 16 pixels keeps generation fast.
CODE_BAND = (
    "color=black:s=16x1:r={fps},format=gray,"
    "geq=lum='255*mod(floor(N/pow(2\\,X))\\,2)',"
    "scale={width}:32:flags=neighbor,format=yuv420p[code]"
)


def run(args: list[str]) -> None:
    print(" ".join(args))
    subprocess.run(args, check=True)


def video(out: Path, width: int, height: int, codec_args: list[str], seconds: int = SECONDS) -> None:
    graph = (
        f"testsrc2=s={width}x{height}:r={FPS}:d={seconds},format=yuv420p[bg];"
        + CODE_BAND.format(fps=FPS, width=width)
        + ";[bg][code]overlay=0:0:shortest=1[v]"
    )
    run([
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-filter_complex", graph,
        "-f", "lavfi", "-i", f"sine=frequency=440:sample_rate=48000:duration={seconds}",
        "-map", "[v]", "-map", "0:a",
        *codec_args,
        "-g", str(GOP), "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "96k",
        "-movflags", "+faststart",
        str(out),
    ])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path, help="folder for the clips (created if missing)")
    args = parser.parse_args()
    out: Path = args.output
    out.mkdir(parents=True, exist_ok=True)

    video(out / "h264-1080p30.mp4", 1920, 1080, ["-c:v", "libx264", "-preset", "medium", "-crf", "20"])
    video(out / "h265-1080p30.mp4", 1920, 1080, ["-c:v", "libx265", "-preset", "fast", "-crf", "24", "-tag:v", "hvc1",
                                                 "-x265-params", "log-level=error"])
    video(out / "av1-1080p30.mp4", 1920, 1080, ["-c:v", "libsvtav1", "-preset", "8", "-crf", "32",
                                                "-svtav1-params", "keyint=120"])
    video(out / "h265-2160p30.mp4", 3840, 2160, ["-c:v", "libx265", "-preset", "ultrafast", "-crf", "26", "-tag:v", "hvc1",
                                                 "-x265-params", "log-level=error"], seconds=10)

    # Animated WebP and a larger animated GIF: 320x240, 3 s at 10 fps, looping.
    pattern = "testsrc2=s=320x240:r=10:d=3"
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", pattern,
         "-c:v", "libwebp_anim", "-loop", "0", "-quality", "75", str(out / "animated.webp")])
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", pattern,
         "-filter_complex", "split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0", str(out / "animated-320.gif")])
    return 0


if __name__ == "__main__":
    sys.exit(main())
