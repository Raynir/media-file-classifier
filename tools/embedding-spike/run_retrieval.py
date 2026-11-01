"""Embeds the evaluation corpus with one candidate model and writes a results file for the runner.

Each item gets the mean of its normalized frame vectors (E06-S02), and each query is ranked
by cosine similarity over all items. A second results file ranks animated items by their
best frame instead ("-maxframe"), for comparison. --onnx runs the towers exported by
export_onnx.py (fp32 or int8) instead of the reference model. Frame vectors are cached under
models/cache, so a rerun only encodes the queries.

Usage (from the repository root, with the spike's virtual environment):
    python tools/embedding-spike/run_retrieval.py siglip2-base-patch16-256
    dotnet run --project tools/MediaFileClassifier.Evaluation -- evaluate --results models/results/siglip2-base-patch16-256.json
"""

import argparse
import json
import time

import numpy as np

from corpus import REPO_ROOT, load_frames, load_items, load_queries
from encoders import ENCODERS, OnnxTowerEncoder

MODELS_DIR = REPO_ROOT / "models"
TOP_K = 100


def embed_frames(encoder, items, batch: int) -> tuple[np.ndarray, np.ndarray]:
    """Returns every frame vector and, for each frame, the index of its item."""
    vectors, owners, pending, pending_owners = [], [], [], []
    for index, item in enumerate(items):
        frames = load_frames(item)
        pending.extend(frames)
        pending_owners.extend([index] * len(frames))
        if len(pending) >= batch or index == len(items) - 1:
            vectors.append(encoder.encode_images(pending))
            owners.extend(pending_owners)
            pending, pending_owners = [], []
    return np.concatenate(vectors), np.array(owners)


def rank(query_vectors, item_ids, frame_vectors, owners, reduce) -> list[list[str]]:
    similarities = query_vectors @ frame_vectors.T
    if reduce == "mean":
        item_vectors = np.stack([frame_vectors[owners == i].mean(axis=0) for i in range(len(item_ids))])
        item_vectors /= np.linalg.norm(item_vectors, axis=1, keepdims=True)
        scores = query_vectors @ item_vectors.T
    else:
        scores = np.stack([similarities[:, owners == i].max(axis=1) for i in range(len(item_ids))], axis=1)
    order = np.argsort(-scores, axis=1, kind="stable")[:, :TOP_K]
    return [[item_ids[i] for i in row] for row in order]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", choices=sorted(ENCODERS))
    parser.add_argument("--reembed", action="store_true", help="ignore the cached frame vectors")
    parser.add_argument("--batch", type=int, default=16, help="frames and queries per call; lower it when a model doesn't fit in GPU memory")
    parser.add_argument("--lowercase", action="store_true", help="lower-case the queries before tokenizing")
    parser.add_argument("--onnx", metavar="IMAGE:TEXT", help="run the exported towers instead, e.g. fp32:int8 (CPU provider)")
    args = parser.parse_args()

    items, queries = load_items(), load_queries()
    name = args.model if not args.onnx else args.model + "-onnx-" + args.onnx.replace(":", "-")
    cache = MODELS_DIR / "cache" / f"{name}.npz"
    results_dir = MODELS_DIR / "results"
    cache.parent.mkdir(parents=True, exist_ok=True)
    results_dir.mkdir(parents=True, exist_ok=True)

    started = time.perf_counter()
    encoder = ENCODERS[args.model]()
    if args.onnx:
        image_variant, text_variant = args.onnx.split(":")
        encoder = OnnxTowerEncoder(encoder, MODELS_DIR / "onnx" / args.model, image_variant, text_variant, ["CPUExecutionProvider"])
    print(f"Loaded {args.model} in {time.perf_counter() - started:.1f} s")

    if cache.exists() and not args.reembed:
        cached = np.load(cache)
        frame_vectors, owners = cached["vectors"], cached["owners"]
    else:
        started = time.perf_counter()
        frame_vectors, owners = embed_frames(encoder, items, args.batch)
        print(f"Embedded {len(frame_vectors)} frames of {len(items)} items in {time.perf_counter() - started:.1f} s")
        np.savez(cache, vectors=frame_vectors, owners=owners)

    texts = [q.text.lower() if args.lowercase else q.text for q in queries]
    query_vectors = np.concatenate([encoder.encode_texts(texts[i:i + args.batch]) for i in range(0, len(texts), args.batch)])
    item_ids = [item.id for item in items]
    for reduce, suffix in (("mean", ""), ("max", "-maxframe")):
        system = name + ("-lowercase" if args.lowercase else "") + suffix
        ranked = rank(query_vectors, item_ids, frame_vectors, owners, reduce)
        results = {"system": system, "search": {q.id: ids for q, ids in zip(queries, ranked)}}
        (results_dir / f"{system}.json").write_text(json.dumps(results, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"Dimension {frame_vectors.shape[1]}; results in {results_dir}")


if __name__ == "__main__":
    main()
