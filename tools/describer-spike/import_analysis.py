"""Copies the app's committed describe-1 output (eval/analysis/qwen_qwen3-vl-4b.json, D338) for the spike samples into a
results file, so the page shows the current prompt next to the draft one. It has no timings."""

import json

from common import ROOT, load_samples, save_result


def main():
    analysis = json.loads((ROOT / "eval" / "analysis" / "qwen_qwen3-vl-4b.json").read_text(encoding="utf-8"))
    items = {}
    for sample in load_samples():
        item = analysis["items"].get(sample["id"])
        if not item:
            continue
        items[sample["id"]] = {
            "seconds": None,
            "firstTryValid": True,
            "output": {
                "title": item.get("title"), "title_ru": None,
                "description": item.get("description"), "summary": item.get("summary"),
                "tags": [{"en": tag, "ru": None} for tag in item.get("tags", [])],
                **{field: item.get(field, []) for field in ("objects", "scenes", "actions", "emotions")},
            },
        }
    save_result("qwen3-vl-4b-describe-1", {"system": "qwen3-vl-4b-describe-1", "route": "lmstudio", "model": analysis["model"],
                                           "promptVersion": analysis.get("promptVersion"), "items": items})
    print(f"{len(items)} items")


if __name__ == "__main__":
    main()
