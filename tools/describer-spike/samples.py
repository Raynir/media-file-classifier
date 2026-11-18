"""Picks the ~50 spike samples (evenly spread over each kind's IDs) and writes the describer's input images."""

import json

from common import SAMPLE_COUNTS, SAMPLES, load_manifest, prepare_input


def main():
    items = load_manifest()
    picked = []
    for kind, count in SAMPLE_COUNTS.items():
        of_kind = sorted((item for item in items if item["kind"] == kind), key=lambda item: item["id"])
        step = len(of_kind) / count
        picked += [of_kind[int(i * step + step / 2)] for i in range(count)]
    samples = [{"id": item["id"], "kind": item["kind"], "file": item["file"], "truth": item.get("description", "")}
               for item in picked]
    SAMPLES.parent.mkdir(parents=True, exist_ok=True)
    SAMPLES.write_text(json.dumps(samples, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for sample in samples:
        prepare_input(sample)
    print(f"{len(samples)} samples written to {SAMPLES}")


if __name__ == "__main__":
    main()
