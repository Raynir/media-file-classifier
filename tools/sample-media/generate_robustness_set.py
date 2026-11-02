"""Generates the media identity robustness test set in tests/SampleMedia/robustness (E01-S06-T06).

Each original gets copies that file matching must still recognise (renamed and moved, re-encoded,
resized, converted) and a screenshot that it must not. expected.json lists every derived file with
its original and the expected match tier. The generated files are committed, so CI doesn't need
this script.

Requirements: Python 3 and Pillow (with WebP support).

Usage (from the repository root):
    python tools/sample-media/generate_robustness_set.py tests/SampleMedia/robustness
"""

import argparse
import io
import json
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ORIGINALS = {"photo-a": 11, "photo-b": 23, "photo-c": 37, "photo-d": 41}
WIDTH, HEIGHT = 640, 480


def natural_image(seed: int) -> Image.Image:
    """A photo-like image: noise at several scales with a 1/f falloff, as in natural images, plus soft shapes."""
    rng = random.Random(seed)
    image = Image.new("RGB", (WIDTH, HEIGHT), tuple(rng.randrange(60, 200) for _ in range(3)))
    for scale, weight in ((160, 0.45), (80, 0.3), (40, 0.18), (20, 0.1), (8, 0.05)):
        small = Image.new("RGB", (WIDTH // scale + 2, HEIGHT // scale + 2))
        small.putdata([tuple(rng.randrange(256) for _ in range(3)) for _ in range(small.width * small.height)])
        layer = small.resize((WIDTH + 2 * scale, HEIGHT + 2 * scale), Image.Resampling.BICUBIC)
        layer = layer.crop((scale, scale, scale + WIDTH, scale + HEIGHT))
        image = Image.blend(image, layer, weight)
    draw = ImageDraw.Draw(image)
    for _ in range(5):
        cx, cy = rng.randrange(WIDTH), rng.randrange(HEIGHT)
        r = rng.randrange(30, 140)
        colour = tuple(rng.randrange(256) for _ in range(3))
        if rng.random() < 0.5:
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=colour)
        else:
            draw.rectangle((cx - r, cy - r // 2, cx + r, cy + r // 2), fill=colour)
    return image


def jpeg_bytes(image: Image.Image, quality: int, **kwargs) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, "JPEG", quality=quality, **kwargs)
    return buffer.getvalue()


def screenshot(image: Image.Image) -> Image.Image:
    """The image shown at half size in an app window on a desktop, as a screen capture would show it."""
    screen = Image.new("RGB", (1366, 768), (32, 80, 128))
    draw = ImageDraw.Draw(screen)
    draw.rectangle((0, 728, 1366, 768), fill=(30, 30, 30))  # taskbar
    draw.rectangle((180, 60, 1180, 680), fill=(245, 245, 245))  # window
    draw.rectangle((180, 60, 1180, 92), fill=(220, 220, 220))  # title bar
    draw.rectangle((180, 92, 420, 680), fill=(235, 235, 240))  # side panel
    for row in range(12):
        draw.rectangle((196, 110 + row * 40, 400, 126 + row * 40), fill=(190, 190, 200))
    screen.paste(image.resize((WIDTH // 2 * 3 // 2, HEIGHT // 2 * 3 // 2), Image.Resampling.LANCZOS), (460, 140))
    return screen


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path, help="Folder to write the set to.")
    args = parser.parse_args()
    out: Path = args.output
    (out / "moved" / "deeper").mkdir(parents=True, exist_ok=True)
    cases = []

    def add(name: str, data: bytes, original: str, expected: str, note: str) -> None:
        (out / name).write_bytes(data)
        cases.append({"file": name, "original": original, "expected": expected, "note": note})

    for stem, seed in ORIGINALS.items():
        image = natural_image(seed)
        original = f"{stem}.jpg"
        data = jpeg_bytes(image, 90)
        (out / original).write_bytes(data)

        def save(fmt: str, img: Image.Image = image, **kwargs) -> bytes:
            buffer = io.BytesIO()
            img.save(buffer, fmt, **kwargs)
            return buffer.getvalue()

        add(f"moved/deeper/renamed-{stem}.jpg", data, original, "content-hash", "byte-identical copy, renamed and moved")
        add(f"{stem}-q50.jpg", jpeg_bytes(image, 50), original, "perceptual", "re-encoded JPEG, quality 50")
        add(f"{stem}-q20.jpg", jpeg_bytes(image, 20), original, "perceptual", "re-encoded JPEG, quality 20")
        add(f"{stem}-half.jpg", jpeg_bytes(image.resize((WIDTH // 2, HEIGHT // 2), Image.Resampling.LANCZOS), 85),
            original, "perceptual", "resized to 50 %")
        add(f"{stem}-thumb.jpg", jpeg_bytes(image.resize((160, 120), Image.Resampling.BILINEAR), 75),
            original, "perceptual", "160x120 thumbnail")
        add(f"{stem}.webp", save("WEBP", quality=75), original, "perceptual", "converted to lossy WebP")
        add(f"{stem}.png", save("PNG", optimize=True), original, "perceptual", "converted to PNG")
        add(f"{stem}-screenshot.png", save("PNG", screenshot(image), optimize=True), original, "none",
            "screenshot of the image in an app window")

    # Pixels stored rotated, with an EXIF orientation that turns them back: displays the same as the original.
    exif = Image.Exif()
    exif[0x0112] = 6  # rotate 90° clockwise to display
    rotated = natural_image(ORIGINALS["photo-a"]).transpose(Image.Transpose.ROTATE_90)
    add("photo-a-exif-rotated.jpg", jpeg_bytes(rotated, 90, exif=exif), "photo-a.jpg", "perceptual",
        "pixels stored rotated, EXIF orientation 6")

    # The animated sample converted to an animated WebP: matched by its first frame.
    gif = Image.open(Path(__file__).resolve().parents[2] / "tests" / "SampleMedia" / "gif-animated.gif")
    frames = []
    for index in range(gif.n_frames):
        gif.seek(index)
        frames.append(gif.convert("RGB"))
    buffer = io.BytesIO()
    frames[0].save(buffer, "WEBP", save_all=True, append_images=frames[1:], duration=100, loop=0, quality=80)
    add("gif-animated.webp", buffer.getvalue(), "../gif-animated.gif", "perceptual",
        "the animated GIF sample as an animated WebP")

    (out / "expected.json").write_text(json.dumps(cases, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    for case in cases:
        print(f"{case['file']}: {(out / case['file']).stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
