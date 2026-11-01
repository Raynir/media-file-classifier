"""Compares quantized variants of one model's text tower against the same image vectors.

The image vectors come from a previous run_retrieval.py --onnx fp32:... cache, so only the
text tower changes between rows. Variants are written next to the exported towers:
  text_int8.onnx      dynamic int8, per tensor (export_onnx.py)
  text_int8pc.onnx    dynamic int8, per channel
  text_int8mm.onnx    dynamic int8 on MatMul only; the token embedding table stays fp32
  text_fp16.onnx      float16 weights, float32 inputs and outputs
Any other text_*.onnx in the folder (for example a publisher export) is scored too.

Usage (from the repository root, with the spike's virtual environment):
    python tools/embedding-spike/compare_text_towers.py siglip2-base-patch16-256 --lowercase
"""

import argparse
import json
import os

os.environ.setdefault("MFC_SPIKE_DEVICE", "cpu")

import numpy as np  # noqa: E402
from onnxruntime.quantization import QuantType, quantize_dynamic  # noqa: E402

from corpus import EVAL_DIR, REPO_ROOT, load_items, load_queries  # noqa: E402
from encoders import ENCODERS, _normalize, ort_session  # noqa: E402
from run_retrieval import rank  # noqa: E402


def make_variants(folder) -> None:
    source = folder / "text.onnx"
    if not (folder / "text_int8pc.onnx").exists():
        quantize_dynamic(source, folder / "text_int8pc.onnx", weight_type=QuantType.QInt8, per_channel=True)
    if not (folder / "text_int8mm.onnx").exists():
        quantize_dynamic(source, folder / "text_int8mm.onnx", weight_type=QuantType.QInt8, op_types_to_quantize=["MatMul"])
    if not (folder / "text_fp16.onnx").exists():
        import onnx
        from onnxconverter_common import float16

        model = onnx.load(source)
        onnx.save(float16.convert_float_to_float16(model, keep_io_types=True), folder / "text_fp16.onnx")


def recall_at_10(ranked, queries, labels, language) -> float:
    rows = [(q, r) for q, r in zip(queries, ranked) if q.language == language]
    return sum(len(set(labels[q.id]) & set(r[:10])) / min(len(labels[q.id]), 10) for q, r in rows) / len(rows)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", choices=sorted(ENCODERS))
    parser.add_argument("--lowercase", action="store_true")
    args = parser.parse_args()
    folder = REPO_ROOT / "models" / "onnx" / args.model
    make_variants(folder)

    cached = np.load(REPO_ROOT / "models" / "cache" / f"{args.model}-onnx-fp32-int8.npz")
    items, queries = load_items(), load_queries()
    labels = {q["id"]: q["relevant"] for q in json.loads((EVAL_DIR / "queries.json").read_text(encoding="utf-8"))}
    encoder = ENCODERS[args.model]()
    texts = [q.text.lower() if args.lowercase else q.text for q in queries]
    tokens = [encoder.prepare_texts([t]) for t in texts]

    print("| Text tower | MB | R@10 en | R@10 ru |\n|---|--:|--:|--:|")
    for path in sorted(folder.glob("text*.onnx")):
        session = ort_session(path, ["CPUExecutionProvider"])
        names = [i.name for i in session.get_inputs()]
        outputs = [o.name for o in session.get_outputs()]
        output = "pooler_output" if "pooler_output" in outputs else outputs[0]
        vectors = _normalize(np.concatenate([session.run([output], {n: t[n] for n in names})[0] for t in tokens]))
        ranked = rank(vectors, [i.id for i in items], cached["vectors"], cached["owners"], "mean")
        size = sum(p.stat().st_size for p in folder.glob(path.stem + ".onnx*")) / 2**20
        print(f"| {path.stem} | {size:.0f} | {recall_at_10(ranked, queries, labels, 'en'):.3f} | {recall_at_10(ranked, queries, labels, 'ru'):.3f} |", flush=True)


if __name__ == "__main__":
    main()
