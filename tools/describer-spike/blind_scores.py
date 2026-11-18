"""Blind scoring of the describer outputs by reviewers that don't know which system wrote what.

  blind_scores.py packets <dir> system...   writes <dir>/batch-1..5.md, the images and <dir>/secret/mapping.json
  blind_scores.py total <dir>               maps <dir>/scores-1..5.json back, prints the totals, writes the scores

Each batch has 10 samples; each sample shows its image (photos, GIFs and video frames at 512 px, screenshots and
documents at 1024 px so the text stays readable) and every system's output under a label A-H, shuffled per sample.
A reviewer reads only its batch and writes scores-<n>.json keyed "<sample>|<label>", each value
{"score": 1|2|3, "reason", "halluc", "ru": "ok|poor|none", "missing": [...]}. Keep secret/ away from the reviewers.
`total` writes eval/describer-spike/blind-scores.json keyed "<sample>|<system>" like the page's localStorage key
mfc105-scores, so the scores can be loaded into the side-by-side page.
"""

import argparse
import collections
import json
import random
import statistics
from pathlib import Path

from PIL import Image

from common import SPIKE, input_path, load_result, load_samples

BATCHES = 5
LABELS = "ABCDEFGH"
KINDS = ["photo", "screenshot", "document", "gif", "video"]


def packets(folder, systems):
    if len(systems) > len(LABELS):
        raise SystemExit(f"at most {len(LABELS)} systems")
    results = {system: load_result(system) for system in systems}
    rng = random.Random(105)
    samples = load_samples()
    rng.shuffle(samples)
    (folder / "img").mkdir(parents=True, exist_ok=True)
    (folder / "secret").mkdir(exist_ok=True)
    mapping = {}
    for number in range(BATCHES):
        lines = []
        for sample in samples[number::BATCHES]:
            sid, kind = sample["id"], sample["kind"]
            size = 1024 if kind in ("screenshot", "document") else 512
            image = Image.open(input_path(sid)).convert("RGB")
            image.thumbnail((size, size))
            image_path = folder / "img" / f"{sid}.jpg"
            image.save(image_path, quality=88)
            labels = list(LABELS[:len(systems)])
            rng.shuffle(labels)
            mapping[sid] = dict(zip(labels, systems))
            frame = ", middle frame" if kind in ("gif", "video") else ""
            lines.append(f"## Sample {sid} ({kind}{frame})\nImage: {image_path}\n")
            for label in sorted(mapping[sid]):
                item = results[mapping[sid][label]]["items"].get(sid) or {}
                output = item.get("output")
                body = json.dumps(output, ensure_ascii=False) if output else \
                    f"NO USABLE ANSWER ({item.get('retryError') or item.get('error')})"
                lines.append(f"### System {label}\n{body}\n")
        (folder / f"batch-{number + 1}.md").write_text("\n".join(lines), encoding="utf-8")
    (folder / "secret" / "mapping.json").write_text(json.dumps(mapping, indent=1), encoding="utf-8")


def total(folder):
    mapping = json.loads((folder / "secret" / "mapping.json").read_text(encoding="utf-8"))
    kinds = {sample["id"]: sample["kind"] for sample in load_samples()}
    scores = {}
    for number in range(1, BATCHES + 1):
        for key, value in json.loads((folder / f"scores-{number}.json").read_text(encoding="utf-8")).items():
            sid, label = key.split("|")
            scores[f"{sid}|{mapping[sid][label]}"] = value
    (SPIKE / "blind-scores.json").write_text(json.dumps(scores, ensure_ascii=False, indent=1), encoding="utf-8")
    systems = sorted({key.split("|", 1)[1] for key in scores})
    print("| System | total | mean | 3s | 1s | hallucinations | Russian ok/poor/none | " + " | ".join(KINDS) + " |")
    print("|" + "---|" * (7 + len(KINDS)))
    rows = []
    for system in systems:
        values = [(key.split("|")[0], v) for key, v in scores.items() if key.split("|", 1)[1] == system]
        points = [v["score"] for _, v in values]
        russian = collections.Counter(v["ru"] for _, v in values)
        per_kind = []
        for kind in KINDS:
            kind_points = [v["score"] for sid, v in values if kinds[sid] == kind]
            per_kind.append(f"{statistics.mean(kind_points):.2f}" if kind_points else "–")
        rows.append((sum(points), f"| {system} | {sum(points)}/{3 * len(points)} | {statistics.mean(points):.2f} | "
                                  f"{points.count(3)} | {points.count(1)} | {sum(v['halluc'] for _, v in values)} | "
                                  f"{russian['ok']}/{russian['poor']}/{russian['none']} | " + " | ".join(per_kind) + " |"))
    for _, row in sorted(rows, reverse=True):
        print(row)
    print("\nSamples where the systems disagree most (standard deviation of the scores):")
    spread = []
    for sid in kinds:
        by_system = {s: scores[f"{sid}|{s}"]["score"] for s in systems if f"{sid}|{s}" in scores}
        if by_system:
            spread.append((statistics.pstdev(by_system.values()), sid, by_system))
    for deviation, sid, by_system in sorted(spread, reverse=True)[:10]:
        print(f"{sid} ({kinds[sid]}) {deviation:.2f}: " + ", ".join(f"{s}={v}" for s, v in by_system.items()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["packets", "total"])
    parser.add_argument("folder", type=Path)
    parser.add_argument("systems", nargs="*")
    args = parser.parse_args()
    if args.command == "packets":
        packets(args.folder, args.systems)
    else:
        total(args.folder)


if __name__ == "__main__":
    main()
