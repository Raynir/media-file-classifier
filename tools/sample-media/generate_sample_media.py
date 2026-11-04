"""Generates the edge-case sample media corpus in tests/SampleMedia (E01-S11).

The generated files are committed, so CI doesn't need this script. Rerun it only to
change the corpus, then update tests/SampleMedia/README.md and SampleMedia.cs.

Requirements: Python 3, Pillow, and ffmpeg on PATH. The OCR image uses Arial from
the Windows fonts folder.

Usage (from the repository root):
    python tools/sample-media/generate_sample_media.py tests/SampleMedia [file ...]

Name files to regenerate only those; the others stay byte-for-byte as committed.
"""

import argparse
import os
import random
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageCms, ImageDraw, ImageFont

SEED = 52


def gradient(width: int, height: int) -> Image.Image:
    """A photo-like RGB image: a diagonal gradient with a few soft shapes and noise."""
    rng = random.Random(SEED)
    image = Image.new("RGB", (width, height))
    pixels = image.load()
    for y in range(height):
        for x in range(width):
            pixels[x, y] = (
                int(255 * x / width),
                int(255 * y / height),
                int(255 * (1 - x / width) * (1 - y / height)),
            )
    draw = ImageDraw.Draw(image)
    for _ in range(6):
        cx, cy = rng.randrange(width), rng.randrange(height)
        r = rng.randrange(20, min(width, height) // 3)
        colour = tuple(rng.randrange(256) for _ in range(3))
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=colour)
    for _ in range(width * height // 20):
        x, y = rng.randrange(width), rng.randrange(height)
        value = rng.randrange(256)
        pixels[x, y] = (value, value, value)
    return image


def jpeg_with_exif(path: Path) -> None:
    exif = Image.Exif()
    exif[0x010F] = "MFC Test Camera Co."  # Make
    exif[0x0110] = "Sample 1"  # Model
    exif[0x0112] = 6  # Orientation: rotate 90° clockwise to display
    exif[0x0131] = "generate_sample_media.py"  # Software
    exif[0x0132] = "2026:10:29 12:34:56"  # DateTime
    exif_ifd = exif.get_ifd(0x8769)
    exif_ifd[0x9003] = "2026:10:29 12:34:56"  # DateTimeOriginal
    exif_ifd[0x9004] = "2026:10:29 12:34:56"  # DateTimeDigitized
    gps_ifd = exif.get_ifd(0x8825)
    gps_ifd[0x0000] = b"\x02\x03\x00\x00"  # GPSVersionID
    gps_ifd[0x0001] = "S"  # Cape Horn, 55°58'48" S: southern and western refs test the sign handling
    gps_ifd[0x0002] = (55.0, 58.0, 48.0)
    gps_ifd[0x0003] = "W"  # 67°17'21" W
    gps_ifd[0x0004] = (67.0, 17.0, 21.0)
    gradient(640, 480).save(path, "JPEG", quality=85, exif=exif)


def jpeg_with_icc_and_offset(path: Path) -> None:
    exif = Image.Exif()
    exif[0x010F] = "MFC Test Camera Co."  # Make
    exif[0x0110] = "Sample 2"  # Model
    exif_ifd = exif.get_ifd(0x8769)
    exif_ifd[0x9003] = "2026:06:15 08:30:00"  # DateTimeOriginal
    exif_ifd[0x9011] = "+03:00"  # OffsetTimeOriginal (EXIF 2.31)
    exif_ifd[0x9291] = "25"  # SubSecTimeOriginal: 250 ms
    exif_ifd[0xA001] = 1  # ColorSpace: sRGB
    exif_ifd[0xA434] = "MFC Lens 35mm"  # LensModel
    icc = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
    gradient(200, 100).save(path, "JPEG", quality=80, exif=exif, icc_profile=icc)


def png_with_alpha(path: Path) -> None:
    image = gradient(256, 256).convert("RGBA")
    alpha = Image.new("L", image.size)
    ImageDraw.Draw(alpha).ellipse((16, 16, 240, 240), fill=255)
    image.putalpha(alpha)
    image.save(path, "PNG", optimize=True)


def webp(path: Path) -> None:
    gradient(320, 240).save(path, "WEBP", quality=75)


def animated_webp(path: Path) -> None:
    frames = [gif_frame(i, 6).convert("RGB") for i in range(6)]
    frames[0].save(path, "WEBP", save_all=True, append_images=frames[1:], duration=120, loop=0, quality=60)


def gif_frame(index: int, count: int, size: int = 96) -> Image.Image:
    image = Image.new("RGB", (size, size), (240, 240, 240))
    draw = ImageDraw.Draw(image)
    offset = int((size - 32) * index / max(count - 1, 1))
    draw.rectangle((offset, offset, offset + 32, offset + 32), fill=(200, 40, 40))
    return image.convert("P", palette=Image.Palette.ADAPTIVE, colors=16)


def static_gif(path: Path) -> None:
    gif_frame(0, 1).save(path, "GIF")


def animated_gif(path: Path) -> None:
    frames = [gif_frame(i, 8) for i in range(8)]
    frames[0].save(path, "GIF", save_all=True, append_images=frames[1:], duration=100, loop=0)


def gif_blank_first_frame(path: Path) -> None:
    # Like a GIF that fades in from an empty canvas: the first frame is one flat colour.
    blank = Image.new("RGB", (96, 96), (255, 255, 255)).convert("P", palette=Image.Palette.ADAPTIVE, colors=16)
    frames = [blank] + [gif_frame(i, 5) for i in range(5)]
    frames[0].save(path, "GIF", save_all=True, append_images=frames[1:], duration=100, loop=0, optimize=False)


def ocr_text(path: Path) -> None:
    font_path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "arial.ttf"
    font = ImageFont.truetype(str(font_path), 40)
    image = Image.new("L", (640, 200), 255)
    draw = ImageDraw.Draw(image)
    draw.text((30, 40), "Invoice 2026-10-29", font=font, fill=0)
    draw.text((30, 110), "Счёт на оплату", font=font, fill=0)
    image.save(path, "PNG", optimize=True)


def corrupt_jpeg(path: Path) -> None:
    # A real JPEG start (SOI, APP0) cut off in the middle and padded with junk: no EOI marker.
    data = bytearray()
    source = path.with_suffix(".tmp.jpg")
    gradient(64, 64).save(source, "JPEG", quality=80)
    data += source.read_bytes()[:300]
    source.unlink()
    rng = random.Random(SEED)
    data += bytes(rng.randrange(256) for _ in range(200)).replace(b"\xff\xd9", b"\x00\x00")
    path.write_bytes(bytes(data))


def zero_byte(path: Path) -> None:
    path.write_bytes(b"")


def non_ascii_name(path: Path) -> None:
    gradient(160, 120).save(path, "JPEG", quality=80)


def short_video(path: Path) -> None:
    subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-preset", "veryslow", "-crf", "30", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "48k",
            "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact",
            "-movflags", "+faststart",
            str(path),
        ],
        check=True,
    )


def encode_video(path: Path, inputs: list[str], codec: list[str]) -> None:
    subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            *inputs,
            *codec,
            "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact",
            "-movflags", "+faststart",
            str(path),
        ],
        check=True,
    )


def hevc_video(path: Path) -> None:
    # hvc1 is the sample entry Apple devices write; FFmpeg defaults to hev1.
    encode_video(
        path,
        ["-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1"],
        ["-c:v", "libx265", "-preset", "medium", "-crf", "35", "-pix_fmt", "yuv420p", "-tag:v", "hvc1",
         "-x265-params", "log-level=error"],
    )


def av1_video(path: Path) -> None:
    encode_video(
        path,
        ["-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1"],
        ["-c:v", "libaom-av1", "-cpu-used", "8", "-crf", "50", "-pix_fmt", "yuv420p"],
    )


def portrait_video(path: Path) -> None:
    # Like a phone held upright: landscape pixels with a display matrix that turns them 90° clockwise, and the
    # container's creation time.
    landscape = path.with_suffix(".tmp.mp4")
    encode_video(
        landscape,
        ["-f", "lavfi", "-i", "testsrc=size=160x90:rate=10:duration=1"],
        ["-c:v", "libx264", "-preset", "veryslow", "-crf", "35", "-pix_fmt", "yuv420p"],
    )
    subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-display_rotation", "-90", "-i", str(landscape),
            "-c", "copy", "-map_metadata", "-1", "-metadata", "creation_time=2026-07-01T10:20:30.000000Z",
            "-fflags", "+bitexact", "-movflags", "+faststart",
            str(path),
        ],
        check=True,
    )
    landscape.unlink()


def black_intro_video(path: Path) -> None:
    # 4 s with a 1.5 s black intro: the frames at 10% (0.4 s) and 25% (1 s) are black, the one at 50% isn't.
    encode_video(
        path,
        ["-f", "lavfi", "-i", "color=black:size=160x120:rate=10:duration=1.5",
         "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=2.5",
         "-filter_complex", "[0:v][1:v]concat=n=2:v=1[v]", "-map", "[v]"],
        ["-c:v", "libx264", "-preset", "veryslow", "-crf", "35", "-pix_fmt", "yuv420p"],
    )


def audio_only_mp4(path: Path) -> None:
    encode_video(path, ["-f", "lavfi", "-i", "sine=frequency=440:duration=1"], ["-c:a", "aac", "-b:a", "32k"])


GENERATORS = {
    "jpeg-exif.jpg": jpeg_with_exif,
    "jpeg-icc-offset.jpg": jpeg_with_icc_and_offset,
    "png-alpha.png": png_with_alpha,
    "webp.webp": webp,
    "gif-static.gif": static_gif,
    "gif-animated.gif": animated_gif,
    "webp-animated.webp": animated_webp,
    "gif-blank-first-frame.gif": gif_blank_first_frame,
    "video-short.mp4": short_video,
    "video-hevc.mp4": hevc_video,
    "video-av1.mp4": av1_video,
    "video-portrait.mp4": portrait_video,
    "video-black-intro.mp4": black_intro_video,
    "audio-only.mp4": audio_only_mp4,
    "ocr-text.png": ocr_text,
    "corrupt.jpg": corrupt_jpeg,
    "zero-byte.png": zero_byte,
    "фото с пробелом.jpg": non_ascii_name,
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path, help="Folder to write the corpus to.")
    parser.add_argument("names", nargs="*", help="Files to generate; all of them when omitted.")
    args = parser.parse_args()
    unknown = set(args.names) - GENERATORS.keys()
    if unknown:
        parser.error(f"unknown files: {', '.join(sorted(unknown))}")
    args.output.mkdir(parents=True, exist_ok=True)
    for name, generate in GENERATORS.items():
        if args.names and name not in args.names:
            continue
        generate(args.output / name)
        print(f"{name}: {(args.output / name).stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
