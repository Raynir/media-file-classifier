"""Shared helpers for the describer model spike (MFC-105, E06-SP02).

Samples, input images and result files. Run every script from the repository root with
models/.venv/Scripts/python.
"""

import io
import json
import subprocess
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
CORPUS = ROOT / "eval" / "corpus"
SPIKE = ROOT / "eval" / "describer-spike"
SAMPLES = SPIKE / "samples.json"
RESULTS = SPIKE / "results"
INPUTS = ROOT / "models" / "describer-spike" / "inputs"

# The app describes a still image at 1024 px on its longest edge, and a video or GIF by its middle keyframe
# (ItemAnalyzers, AnalyzeCommand).
DESCRIBE_LONGEST_EDGE = 1024

# Per kind, how many items to take, evenly spread over the IDs: 50 in all.
SAMPLE_COUNTS = {"photo": 22, "screenshot": 5, "document": 5, "gif": 8, "video": 10}


def load_manifest():
    return json.loads((ROOT / "eval" / "manifest.json").read_text(encoding="utf-8"))


def load_samples():
    return json.loads(SAMPLES.read_text(encoding="utf-8"))


def input_path(item_id):
    return INPUTS / f"{item_id}.png"


def prepare_input(item):
    """Writes the image the describer sees for an item (still at 1024 px, or the middle frame) and returns its path."""
    target = input_path(item["id"])
    if target.exists():
        return target
    target.parent.mkdir(parents=True, exist_ok=True)
    source = CORPUS / item["file"]
    if item["kind"] == "video":
        duration = float(subprocess.check_output(
            ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(source)], text=True))
        frame = subprocess.check_output(
            ["ffmpeg", "-v", "error", "-ss", f"{duration / 2:.3f}", "-i", str(source), "-frames:v", "1",
             "-f", "image2pipe", "-vcodec", "png", "-"])
        image = Image.open(io.BytesIO(frame))
    elif item["kind"] == "gif":
        image = Image.open(source)
        image.seek(getattr(image, "n_frames", 1) // 2)
    else:
        image = Image.open(source)
    image = image.convert("RGB")
    image.thumbnail((DESCRIBE_LONGEST_EDGE, DESCRIBE_LONGEST_EDGE), Image.Resampling.LANCZOS)
    image.save(target)
    return target


def media_context(item):
    """The context line the describer gets for a frame (ImageDescriptionOptions.Context)."""
    if item["kind"] == "video":
        return "This is the middle frame of a short video clip."
    if item["kind"] == "gif":
        return "This is the middle frame of an animated GIF."
    return None


def result_path(system):
    return RESULTS / f"{system.replace('/', '_').replace(':', '_')}.json"


def load_result(system):
    path = result_path(system)
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else None


def save_result(system, data):
    path = result_path(system)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)
