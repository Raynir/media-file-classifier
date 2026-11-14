"""Writes the SigLIP 2 reference outputs that the .NET embedding tests compare against (E06-S03-T01 to T03).

The reference is the Hugging Face processor (PIL resize, Gemma tokenizer) feeding the same ONNX towers the app
runs. The model spike showed that these towers give the PyTorch model's vectors (docs/spikes/embedding-model.md).

Usage (from the repository root, with the venv described in README.md):
    models/.venv/Scripts/python -I tools/embedding-reference/make_reference.py models/siglip2

Writes tests/MediaFileClassifier.Infrastructure.Tests/AI/Embeddings/siglip2-reference.json.
"""

import hashlib
import json
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image, ImageOps
from transformers import AutoProcessor

REPO_ROOT = Path(__file__).resolve().parents[2]
SAMPLES = REPO_ROOT / "tests" / "SampleMedia"
TARGET = REPO_ROOT / "tests" / "MediaFileClassifier.Infrastructure.Tests" / "AI" / "Embeddings" / "siglip2-reference.json"
IMAGES = ["jpeg-exif.jpg", "jpeg-icc-offset.jpg", "png-alpha.png", "ocr-text.png", "webp.webp", "gif-static.gif", "фото с пробелом.jpg"]
# PNG and GIF decode identically everywhere, so their resized pixels must match the .NET resampler exactly.
LOSSLESS = {"png-alpha.png", "ocr-text.png", "gif-static.gif"}
QUERY_SAMPLE = ["en-001", "en-010", "en-040", "en-070", "en-100", "ru-001", "ru-010", "ru-040", "ru-070", "ru-100"]


def load_rgb(path: Path) -> Image.Image:
    """Upright RGB with transparent areas composited over white, as the app prepares every picture."""
    image = ImageOps.exif_transpose(Image.open(path))
    image = image.convert("RGBA")
    background = Image.new("RGBA", image.size, (255, 255, 255, 255))
    return Image.alpha_composite(background, image).convert("RGB")


def normalise(vectors: np.ndarray) -> np.ndarray:
    return vectors / np.linalg.norm(vectors, axis=-1, keepdims=True)


def main() -> None:
    model_dir = Path(sys.argv[1])
    processor = AutoProcessor.from_pretrained("google/siglip2-base-patch16-256", backend="pil")
    vision = ort.InferenceSession(str(model_dir / "vision_model.onnx"), providers=["CPUExecutionProvider"])
    text = ort.InferenceSession(str(model_dir / "text_model.onnx"), providers=["CPUExecutionProvider"])

    images = []
    for name in IMAGES:
        rgb = load_rgb(SAMPLES / name)
        pixels = processor.image_processor(images=[rgb], return_tensors="np")["pixel_values"].astype(np.float32)
        vector = normalise(vision.run(["pooler_output"], {"pixel_values": pixels})[0])[0]
        entry = {"file": name, "vector": [round(float(v), 6) for v in vector]}
        if name in LOSSLESS:
            resized = rgb.resize((256, 256), Image.Resampling.BILINEAR)
            entry["resizedSha256"] = hashlib.sha256(resized.tobytes()).hexdigest()
        images.append(entry)

    queries = json.loads((REPO_ROOT / "eval" / "queries.json").read_text(encoding="utf-8"))
    tokens = []
    texts = []
    for query in queries:
        lowered = query["text"].lower()
        ids = processor(text=[lowered], padding="max_length", max_length=64, truncation=True, return_tensors="np")["input_ids"].astype(np.int64)
        tokens.append({"id": query["id"], "text": query["text"], "ids": [int(i) for i in ids[0]]})
        if query["id"] in QUERY_SAMPLE:
            vector = normalise(text.run(["pooler_output"], {"input_ids": ids})[0])[0]
            texts.append({"id": query["id"], "text": query["text"], "vector": [round(float(v), 6) for v in vector]})

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(json.dumps({"images": images, "texts": texts, "tokens": tokens}, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {TARGET}: {len(images)} images, {len(texts)} text vectors, {len(tokens)} token cases")


if __name__ == "__main__":
    main()
