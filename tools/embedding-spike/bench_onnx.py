"""Measures the exported ONNX towers of one candidate: file sizes, image throughput and text latency.

Runs on ONNX Runtime with the CPU provider (8 threads, to stand in for the D031 CPU tier,
and all cores) and the CUDA provider. Image throughput is the model call only, on a batch
of corpus photos already preprocessed; decoding and resizing are not included. Text latency
is one query per call, the search-as-you-type case. Writes models/bench/<model>.json.

Usage (from the repository root, with the spike's virtual environment, after export_onnx.py):
    python tools/embedding-spike/bench_onnx.py siglip2-base-patch16-256
"""

import argparse
import gc
import json
import os
import statistics
import time

os.environ.setdefault("MFC_SPIKE_DEVICE", "cpu")

from corpus import REPO_ROOT, load_frames, load_items  # noqa: E402
from encoders import ENCODERS, JinaClipV2Encoder, MexmaSiglip2Encoder, ort_session  # noqa: E402

ONNX_DIR = REPO_ROOT / "models" / "onnx"
BENCH_DIR = REPO_ROOT / "models" / "bench"
QUERIES = ["a red car parked next to an old house", "красная машина у старого дома"]
CPU = ["CPUExecutionProvider"]
CUDA = ["CUDAExecutionProvider", "CPUExecutionProvider"]


def base_encoder(model: str):
    if model == "mexma-siglip2":
        return MexmaSiglip2Encoder(sessions=False)
    if model == "jina-clip-v2":
        return JinaClipV2Encoder(sessions=False)
    return ENCODERS[model]()


def file_size(folder, name: str) -> float:
    paths = [folder / name, folder / (name + ".data"), folder / (name + "_data"), folder / name.replace(".onnx", "_model.onnx_data")]
    return round(sum(p.stat().st_size for p in paths if p.exists()) / 2**20, 1)


def timed(run, repeats: int) -> list[float]:
    run()
    run()
    times = []
    for _ in range(repeats):
        started = time.perf_counter()
        run()
        times.append((time.perf_counter() - started) * 1000)
    return times


def image_throughput(path, providers, threads, pixels) -> float:
    session = ort_session(path, providers, threads)
    name = session.get_inputs()[0].name
    times = timed(lambda: session.run(None, {name: pixels}), 5)
    return round(len(pixels) * 1000 / statistics.median(times), 1)


def text_latency(path, providers, threads, token_sets) -> float:
    session = ort_session(path, providers, threads)
    names = [i.name for i in session.get_inputs()]
    times = []
    for tokens in token_sets:
        feeds = {n: tokens[n] for n in names}
        times += timed(lambda: session.run(None, feeds), 30)
    return round(statistics.median(times), 1)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", choices=sorted(ENCODERS))
    parser.add_argument("--batch", type=int, default=16)
    args = parser.parse_args()
    folder = ONNX_DIR / args.model
    encoder = base_encoder(args.model)

    photos = [item for item in load_items() if item.kind == "photo"][:args.batch]
    pixels = encoder.prepare_images([load_frames(item)[0] for item in photos])
    token_sets = [encoder.prepare_texts([query]) for query in QUERIES]
    # Only the preprocessing was needed; free the PyTorch weights before the ONNX sessions load.
    del encoder
    gc.collect()
    result = {
        "model": args.model,
        "input": list(pixels.shape[1:]),
        "sizeMb": {name: file_size(folder, f"{name}.onnx") for name in ("image", "image_int8", "text", "text_int8")},
        "imagesPerSecond": {},
        "textMs": {},
    }
    for variant in ("", "_int8"):
        label = variant.strip("_") or "fp32"
        for threads in (8, 0):
            key = f"cpu{threads or 'All'}-{label}"
            result["imagesPerSecond"][key] = image_throughput(folder / f"image{variant}.onnx", CPU, threads, pixels)
            result["textMs"][key] = text_latency(folder / f"text{variant}.onnx", CPU, threads, token_sets)
            print(key, result["imagesPerSecond"][key], "img/s,", result["textMs"][key], "ms/query", flush=True)
    result["imagesPerSecond"]["cuda-fp32"] = image_throughput(folder / "image.onnx", CUDA, 0, pixels)
    result["textMs"]["cuda-fp32"] = text_latency(folder / "text.onnx", CUDA, 0, token_sets)
    print("cuda-fp32", result["imagesPerSecond"]["cuda-fp32"], "img/s,", result["textMs"]["cuda-fp32"], "ms/query")

    BENCH_DIR.mkdir(parents=True, exist_ok=True)
    (BENCH_DIR / f"{args.model}.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result["sizeMb"]))


if __name__ == "__main__":
    main()
