"""Loads the evaluation corpus (E01-S10): manifest, queries and the frames of each item.

Photos, screenshots and documents give one frame. Animated GIFs and videos give up to
FRAME_COUNT frames at even intervals, the way E06-S02 samples keyframes.
"""

import json
from dataclasses import dataclass
from pathlib import Path

import av
from PIL import Image, ImageSequence

REPO_ROOT = Path(__file__).resolve().parents[2]
EVAL_DIR = REPO_ROOT / "eval"
CORPUS_DIR = EVAL_DIR / "corpus"
FRAME_COUNT = 8


@dataclass
class Item:
    id: str
    kind: str
    path: Path


@dataclass
class Query:
    id: str
    language: str
    category: str
    text: str


def load_items() -> list[Item]:
    manifest = json.loads((EVAL_DIR / "manifest.json").read_text(encoding="utf-8"))
    return [Item(entry["id"], entry["kind"], CORPUS_DIR / entry["file"]) for entry in manifest]


def load_queries() -> list[Query]:
    queries = json.loads((EVAL_DIR / "queries.json").read_text(encoding="utf-8"))
    return [Query(q["id"], q["language"], q["category"], q["text"]) for q in queries]


def _even_positions(count: int, total: int) -> list[int]:
    if total <= count:
        return list(range(total))
    return [int((i + 0.5) * total / count) for i in range(count)]


def _flatten(image: Image.Image) -> Image.Image:
    """Composites transparency on white and returns an RGB image."""
    if image.mode in ("RGBA", "LA", "P"):
        rgba = image.convert("RGBA")
        background = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        return Image.alpha_composite(background, rgba).convert("RGB")
    return image.convert("RGB")


def _gif_frames(path: Path) -> list[Image.Image]:
    with Image.open(path) as gif:
        frames = [_flatten(frame.copy()) for frame in ImageSequence.Iterator(gif)]
    return [frames[i] for i in _even_positions(FRAME_COUNT, len(frames))]


def _video_frames(path: Path) -> list[Image.Image]:
    with av.open(str(path)) as container:
        total = container.streams.video[0].frames
    if total <= 0:
        with av.open(str(path)) as container:
            total = sum(1 for _ in container.decode(video=0))
    wanted = set(_even_positions(FRAME_COUNT, total))
    with av.open(str(path)) as container:
        return [frame.to_image() for index, frame in enumerate(container.decode(video=0)) if index in wanted]


def load_frames(item: Item) -> list[Image.Image]:
    if item.kind == "gif":
        return _gif_frames(item.path)
    if item.kind == "video":
        return _video_frames(item.path)
    with Image.open(item.path) as image:
        return [_flatten(image)]
