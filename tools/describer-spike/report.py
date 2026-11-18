"""Summarises the spike results and writes the side-by-side page for judging quality by hand.

  report.py [--html <path>] system...

Prints a Markdown table (fields filled, JSON validity, seconds per image, memory) for docs/spikes/describer-model.md, and
writes an HTML page with every sample's input image and each system's output next to each other. The page has a
1-3 score per sample and system, kept in the browser and copied out as JSON with a button.
"""

import argparse
import html
import json
import statistics

from common import ROOT, input_path, load_result, load_samples

LIST_FIELDS = ["objects", "scenes", "actions", "emotions"]
TEXT_FIELDS = ["title", "title_ru", "description", "summary"]


def filled(output, field):
    if not output:
        return False
    value = output.get(field)
    if field == "tags":
        return bool(value)
    if field == "tags_ru":
        return bool(output.get("tags")) and all(tag.get("ru") for tag in output["tags"])
    return bool(value.strip()) if isinstance(value, str) else bool(value)


def summarise(result, sample_ids):
    items = [result["items"][i] for i in sample_ids if i in result["items"]]
    seconds = sorted(item["seconds"] for item in items if item.get("seconds") is not None)
    outputs = [item.get("output") for item in items]
    row = {
        "system": result["system"],
        "n": len(items),
        "firstTryValid": sum(1 for item in items if item.get("firstTryValid")) / max(len(items), 1),
        "validAfterRetry": sum(1 for output in outputs if output) / max(len(items), 1),
        "median": statistics.median(seconds) if seconds else None,
        "mean": statistics.mean(seconds) if seconds else None,
        "p90": seconds[int(len(seconds) * 0.9) - 1] if seconds else None,
        "fields": {field: sum(filled(o, field) for o in outputs) / max(len(items), 1)
                   for field in TEXT_FIELDS + ["tags", "tags_ru"] + LIST_FIELDS},
        "tagsPerItem": statistics.mean(len((o or {}).get("tags") or []) for o in outputs) if outputs else 0,
        "load": result.get("load", {}),
    }
    return row


def markdown(rows):
    head = "| System | n | Valid 1st try | Valid after retry | s/image median (mean, p90) | " + " | ".join(
        ["title", "title_ru", "descr.", "summary", "tags", "tags ru", "objects", "scenes", "actions", "emotions"]) + " | tags/item | memory |"
    lines = [head, "|" + "---|" * (head.count("|") - 1)]
    for row in rows:
        f = row["fields"]
        load = row["load"]
        memory = f"{load['gpuModelMiB']:,} MiB GPU" if load.get("gpuModelMiB") else (f"{load['peakWorkingSetMiB']:,} MiB RAM" if load.get("peakWorkingSetMiB") else "")
        timing = f"{row['median']:.1f} ({row['mean']:.1f}, {row['p90']:.1f})" if row["median"] is not None else ""
        cells = [f"{f[k]:.0%}" for k in ["title", "title_ru", "description", "summary", "tags", "tags_ru", "objects", "scenes", "actions", "emotions"]]
        lines.append(f"| {row['system']} | {row['n']} | {row['firstTryValid']:.0%} | {row['validAfterRetry']:.0%} | {timing} | "
                     + " | ".join(cells) + f" | {row['tagsPerItem']:.1f} | {memory} |")
    return "\n".join(lines)


def render_output(output, error):
    if not output:
        return f"<p class='bad'>No usable answer: {html.escape(str(error))}</p>"
    e = html.escape
    parts = []
    if output.get("title") or output.get("title_ru"):
        parts.append(f"<p class='title'>{e(output.get('title') or '—')}<br><span class='ru'>{e(output.get('title_ru') or '—')}</span></p>")
    parts.append(f"<p><b>Summary:</b> {e(output.get('summary') or '—')}</p>")
    parts.append(f"<p><b>Description:</b> {e(output.get('description') or '—')}</p>")
    tags = output.get("tags") or []
    parts.append("<p><b>Tags:</b> " + ", ".join(
        f"{e(t.get('en') or '')}" + (f" <span class='ru'>/ {e(t['ru'])}</span>" if t.get("ru") else "") for t in tags) + "</p>")
    for field in LIST_FIELDS:
        values = output.get(field) or []
        if values:
            parts.append(f"<p><b>{field.capitalize()}:</b> {e(', '.join(values))}</p>")
    return "".join(parts)


def page(samples, results, rows):
    e = html.escape
    systems = [r["system"] for r in results]
    cards = []
    for sample in samples:
        cells = []
        for result in results:
            item = result["items"].get(sample["id"])
            if not item:
                cells.append("<td class='missing'>not run</td>")
                continue
            score = (f"<select data-key='{e(sample['id'])}|{e(result['system'])}'><option value=''>score</option>"
                     "<option>1</option><option>2</option><option>3</option></select>")
            meta = f"<div class='meta'>{item.get('seconds', '?')} s{'' if item.get('firstTryValid') else ' · retried'} {score}</div>"
            cells.append(f"<td>{meta}{render_output(item.get('output'), item.get('retryError') or item.get('error'))}</td>")
        image = input_path(sample["id"]).as_uri()
        cards.append(
            f"<tr><td class='sample'><img src='{image}' loading='lazy'><div><b>{e(sample['id'])}</b> ({e(sample['kind'])})"
            f"<br><i>{e(sample['truth'])}</i></div></td>{''.join(cells)}</tr>")
    header = "".join(f"<th>{e(s)}</th>" for s in systems)
    return f"""<!doctype html><html><head><meta charset="utf-8"><title>Describer spike MFC-105</title>
<style>
body{{font:13px/1.4 system-ui,sans-serif;margin:16px;background:#fff;color:#111}}
table{{border-collapse:collapse;table-layout:fixed}} td,th{{border:1px solid #ccc;vertical-align:top;padding:6px;width:300px}}
th{{position:sticky;top:0;background:#eef}} .sample{{width:260px}} img{{max-width:250px;max-height:220px;display:block}}
p{{margin:3px 0}} .title{{font-weight:600}} .ru{{color:#1a5fb4}} .bad{{color:#b00}} .meta{{color:#666;font-size:11px}}
.missing{{color:#999}} pre{{background:#f6f6f6;padding:8px;overflow:auto}}
</style></head><body>
<h1>Describer model spike (MFC-105): side-by-side outputs</h1>
<p>Each row is one sample: the image the model saw (1024 px, or the middle frame of a GIF or video) and the corpus label in italics.
Give each cell a score: 1 = wrong or useless for search, 2 = usable, 3 = good. Scores stay in this browser; "Copy scores" puts them on the clipboard as JSON to paste back to the session.</p>
<p><button onclick="copyScores()">Copy scores</button> <span id="count"></span></p>
<pre>{e(markdown(rows))}</pre>
<table><tr><th>Sample</th>{header}</tr>{''.join(cards)}</table>
<script>
const KEY='mfc105-scores';
let scores={{}};try{{scores=JSON.parse(localStorage.getItem(KEY)||'{{}}')}}catch(e){{}}
const selects=document.querySelectorAll('select[data-key]');
function count(){{document.getElementById('count').textContent=Object.keys(scores).length+' of '+selects.length+' scored';}}
selects.forEach(s=>{{s.value=scores[s.dataset.key]||'';s.onchange=()=>{{if(s.value)scores[s.dataset.key]=s.value;else delete scores[s.dataset.key];try{{localStorage.setItem(KEY,JSON.stringify(scores))}}catch(e){{}};count();}};}});
count();
function copyScores(){{navigator.clipboard.writeText(JSON.stringify(scores));}}
</script></body></html>"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("systems", nargs="+")
    parser.add_argument("--html")
    args = parser.parse_args()
    samples = load_samples()
    ids = [s["id"] for s in samples]
    results = [r for r in (load_result(s) for s in args.systems) if r]
    rows = [summarise(r, ids) for r in results]
    print(markdown(rows))
    if args.html:
        with open(args.html, "w", encoding="utf-8") as file:
            file.write(page(samples, results, rows))
        print(f"wrote {args.html}")


if __name__ == "__main__":
    main()
