"""Prints the comparison tables of the findings doc from the runner's reports and the benchmarks.

Reads the latest eval/reports/*.json of each system named on the command line, and
models/bench/<model>.json when present.

Usage (from the repository root):
    python tools/embedding-spike/summarize.py siglip2-base-patch16-256-lowercase openai-clip-vit-b16
"""

import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CATEGORIES = ["object", "scene", "synonym", "text", "abstract", "distractor", "media"]
SUFFIXES = ("-lowercase", "-maxframe", "-onnx-fp32-fp32", "-onnx-fp32-int8", "-onnx-int8-int8")


def latest_report(system: str) -> dict:
    reports = sorted((REPO_ROOT / "eval" / "reports").glob(f"*_{system}.json"))
    return json.loads(reports[-1].read_text(encoding="utf-8"))


def model_of(system: str) -> str:
    while system.endswith(SUFFIXES):
        system = next(system[:-len(s)] for s in SUFFIXES if system.endswith(s))
    return system


def main() -> None:
    systems = sys.argv[1:]
    reports = {s: latest_report(s) for s in systems}

    print("| System | R@10 en | MRR en | R@10 ru | MRR ru |")
    print("|---|--:|--:|--:|--:|")
    for system, report in reports.items():
        by = {x["language"]: x for x in report["search"]["languages"]}
        print(f"| {system} | {by['en']['recallAt10']:.3f} | {by['en']['mrr']:.3f} | {by['ru']['recallAt10']:.3f} | {by['ru']['mrr']:.3f} |")

    print("\n| System | " + " | ".join(CATEGORIES) + " |")
    print("|---|" + "--:|" * len(CATEGORIES))
    for system, report in reports.items():
        by = {(x["category"], x["language"]): x["recallAt10"] for x in report["search"]["categories"]}
        print(f"| {system} | " + " | ".join(f"{by[(c, 'en')]:.2f} / {by[(c, 'ru')]:.2f}" for c in CATEGORIES) + " |")

    print("\n| Model | Input | Image fp32 / int8 MB | Text fp32 / int8 MB | CPU 8 thr img/s fp32 / int8 | CUDA img/s | Text ms CPU 8 thr fp32 / int8 | Text ms CUDA |")
    print("|---|---|--:|--:|--:|--:|--:|--:|")
    for model in dict.fromkeys(model_of(s) for s in systems):
        path = REPO_ROOT / "models" / "bench" / f"{model}.json"
        if not path.exists():
            continue
        b = json.loads(path.read_text(encoding="utf-8"))
        size, ips, ms = b["sizeMb"], b["imagesPerSecond"], b["textMs"]
        print(f"| {model} | {b['input'][-1]} px | {size['image']:.0f} / {size['image_int8']:.0f} | {size['text']:.0f} / {size['text_int8']:.0f} "
              f"| {ips['cpu8-fp32']} / {ips['cpu8-int8']} | {ips['cuda-fp32']} | {ms['cpu8-fp32']} / {ms['cpu8-int8']} | {ms['cuda-fp32']} |")


if __name__ == "__main__":
    main()
