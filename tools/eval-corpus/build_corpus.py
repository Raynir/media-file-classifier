"""Builds the public evaluation corpus (E01-S10) from eval/manifest.json.

Maintainers run this only to create a new corpus version; everyone else fetches the
published zip with tools/eval-corpus/fetch-corpus.ps1.

For every Wikimedia Commons item it reads the licence from the Commons API, refuses any
licence outside CC0, Public Domain, CC BY and CC BY-SA, downloads the file, and fills in
the author, licence, attribution and checksum fields of the manifest. Synthetic items
come from generate_synthetic.py, which must run first. At the end it writes
ATTRIBUTION.md next to the files and packs everything into a zip with fixed timestamps.

Requirements: Python 3, Pillow, and ffmpeg on PATH.

Usage (from the repository root):
    python tools/eval-corpus/generate_synthetic.py eval/corpus
    python tools/eval-corpus/build_corpus.py eval/corpus --zip eval-corpus-v1.zip
"""

import argparse
import hashlib
import html
import json
import re
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

from PIL import Image

USER_AGENT = "MediaFileClassifier-eval-corpus/1.0 (https://github.com/Raynir/media-file-classifier)"
API = "https://commons.wikimedia.org/w/api.php"
LONG_SIDE = 1600
ALLOWED_LICENCE = re.compile(r"^(CC0( 1\.0)?|Public domain|CC BY(-SA)? [1-4]\.0( [a-z-]+)?)$", re.IGNORECASE)
ALLOWED_RESTRICTIONS = {"", "personality"}
ZIP_TIME = (2026, 1, 1, 0, 0, 0)


def api(params):
    query = urllib.parse.urlencode({**params, "format": "json", "formatversion": "2"})
    return json.loads(download(f"{API}?{query}"))


def download(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return response.read()
        except Exception as error:  # noqa: BLE001 - retried, then re-raised
            if attempt == 4:
                raise RuntimeError(f"Download failed: {url}") from error
            time.sleep(5 * (attempt + 1))


def plain(value):
    text = re.sub(r"<[^>]+>", "", value or "")
    return re.sub(r"\s+", " ", html.unescape(text)).strip()


def commons_info(titles):
    """Returns imageinfo with licence metadata for up to 50 titles, keyed by title."""
    data = api({
        "action": "query", "titles": "|".join(titles), "prop": "imageinfo",
        "iiprop": "url|size|mime|sha1|extmetadata",
        "iiextmetadatafilter": "LicenseShortName|LicenseUrl|Artist|Credit|Restrictions|AttributionRequired",
    })
    normalized = {n["from"]: n["to"] for n in data["query"].get("normalized", [])}
    pages = {p["title"]: p for p in data["query"]["pages"]}
    return {t: pages.get(normalized.get(t, t)) for t in titles}


def thumb_url(title, width):
    data = api({"action": "query", "titles": title, "prop": "imageinfo", "iiprop": "url", "iiurlwidth": str(width)})
    return data["query"]["pages"][0]["imageinfo"][0]["thumburl"]


def transcode_url(title):
    """The best WebM transcode of at most 720p, or the original."""
    data = api({"action": "query", "titles": title, "prop": "videoinfo", "viprop": "derivatives|url"})
    info = data["query"]["pages"][0]["videoinfo"][0]
    derivatives = [d for d in info.get("derivatives", []) if d.get("height") and d["height"] <= 720 and "webm" in d["type"]]
    if not derivatives:
        return info["url"]
    return max(derivatives, key=lambda d: d["height"])["src"]


def describe_source(item, info):
    page = info
    ii = page["imageinfo"][0]
    meta = {k: v.get("value", "") for k, v in ii.get("extmetadata", {}).items()}
    licence = meta.get("LicenseShortName", "").strip()
    if not ALLOWED_LICENCE.match(licence):
        raise SystemExit(f"{item['id']}: licence '{licence}' is not accepted")
    restrictions = meta.get("Restrictions", "").strip()
    if restrictions not in ALLOWED_RESTRICTIONS:
        raise SystemExit(f"{item['id']}: restrictions '{restrictions}' are not accepted")
    source = {
        "commonsTitle": item["source"]["commonsTitle"],
        "pageUrl": ii["descriptionurl"],
        "author": plain(meta.get("Artist")) or "Unknown",
        "licence": licence,
        "licenceUrl": meta.get("LicenseUrl", ""),
        "originalSha1": ii["sha1"],
    }
    credit = plain(meta.get("Credit"))
    if credit:
        source["credit"] = credit
    if restrictions:
        source["restrictions"] = restrictions
    return source, ii


def fetch_photo(item, ii, target, work, write):
    width, height = ii["width"], ii["height"]
    if max(width, height) <= LONG_SIDE:
        if write:
            target.write_bytes(download(ii["url"]))
        return "Original file, unchanged."
    if write:
        request_width = LONG_SIDE if width >= height else round(LONG_SIDE * width / height)
        target.write_bytes(download(thumb_url(item["source"]["commonsTitle"], request_width)))
    return f"Scaled to {LONG_SIDE} px on the long side by the Wikimedia thumbnailer."


def fetch_gif(item, ii, target, work, write):
    if write:
        target.write_bytes(download(ii["url"]))
    return "Original file, unchanged."


def fetch_video(item, ii, target, work, write):
    clip = item.get("clip", {"start": 0, "duration": 10})
    changes = (f"Cut to {clip['duration']} s from {clip['start']} s and re-encoded as H.264/AAC MP4 "
               "(at most 1280x720) from the Commons WebM transcode.")
    if not write:
        return changes
    source = work / (item["id"] + ".src")
    source.write_bytes(download(transcode_url(item["source"]["commonsTitle"])))
    subprocess.run([
        "ffmpeg", "-y", "-loglevel", "error", "-ss", str(clip["start"]), "-t", str(clip["duration"]),
        "-i", str(source), "-map", "0:v:0", "-map", "0:a:0?",
        "-vf", "scale='min(1280,iw)':'min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2",
        "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-map_metadata", "-1",
        "-fflags", "+bitexact", "-flags:v", "+bitexact", "-flags:a", "+bitexact", str(target),
    ], check=True)
    source.unlink()
    return changes


def media_facts(path, kind):
    facts = {"bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    if kind != "video":
        with Image.open(path) as image:
            facts["width"], facts["height"] = image.size
            if kind == "gif":
                facts["frames"] = getattr(image, "n_frames", 1)
    else:
        probe = subprocess.run(
            ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration",
             "-of", "json", str(path)], check=True, capture_output=True, text=True)
        data = json.loads(probe.stdout)
        facts["width"], facts["height"] = data["streams"][0]["width"], data["streams"][0]["height"]
        facts["durationSeconds"] = round(float(data["format"]["duration"]), 2)
    return facts


def attribution(items):
    lines = [
        "# Evaluation corpus: attribution",
        "",
        "Files from Wikimedia Commons are used under the licence named for each one. "
        "Files marked CC BY-SA, including the changes described, are shared under the same licence. "
        "Synthetic files were made for this project and are dedicated to the public domain under CC0 1.0.",
        "",
        "| File | Title | Author | Licence | Changes |",
        "|------|-------|--------|---------|---------|",
    ]
    for item in items:
        source = item["source"]
        if "commonsTitle" in source:
            title = f"[{source['commonsTitle'][5:]}]({source['pageUrl']})"
            licence = f"[{source['licence']}]({source['licenceUrl']})" if source["licenceUrl"] else source["licence"]
            author = source["author"].replace("|", "/")
        else:
            title, author, licence = "Synthetic", "MediaFileClassifier contributors", "CC0 1.0"
        lines.append(f"| `{item['file']}` | {title.replace('|', '/')} | {author} | {licence} | {item['changes']} |")
    return "\n".join(lines) + "\n"


def write_manifest(path, items):
    with open(path, "w", encoding="utf-8", newline="\n") as stream:
        stream.write("[\n" + ",\n".join("  " + json.dumps(i, ensure_ascii=False) for i in items) + "\n]\n")


def pack(out, items, zip_path):
    names = sorted([i["file"] for i in items] + ["ATTRIBUTION.md"])
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_STORED) as archive:
        for name in names:
            info = zipfile.ZipInfo(name, ZIP_TIME)
            info.external_attr = 0o644 << 16
            archive.writestr(info, (out / name).read_bytes())
    return hashlib.sha256(Path(zip_path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("out", type=Path, help="corpus folder, e.g. eval/corpus")
    parser.add_argument("--manifest", type=Path, default=Path("eval/manifest.json"))
    parser.add_argument("--zip", type=Path, help="also pack the corpus into this zip")
    parser.add_argument("--refresh", action="store_true", help="download files that already exist again")
    args = parser.parse_args()

    items = json.loads(args.manifest.read_text(encoding="utf-8"))
    work = args.out / ".work"
    work.mkdir(parents=True, exist_ok=True)

    commons = [i for i in items if "commonsTitle" in i["source"]]
    infos = {}
    for start in range(0, len(commons), 50):
        infos.update(commons_info([i["source"]["commonsTitle"] for i in commons[start:start + 50]]))

    fetchers = {"photo": fetch_photo, "gif": fetch_gif, "video": fetch_video}
    try:
        for item in items:
            target = args.out / item["file"]
            target.parent.mkdir(parents=True, exist_ok=True)
            if "commonsTitle" in item["source"]:
                source, ii = describe_source(item, infos[item["source"]["commonsTitle"]])
                item["source"] = source
                write = args.refresh or not target.exists()
                item["changes"] = fetchers[item["kind"]](item, ii, target, work, write)
                if write:
                    print(f"{item['id']}: {source['licence']}", flush=True)
            elif not target.exists():
                raise SystemExit(f"{item['id']}: {target} is missing; run generate_synthetic.py first")
            item.update(media_facts(target, item["kind"]))
    finally:
        write_manifest(args.manifest, items)

    work.rmdir()
    (args.out / "ATTRIBUTION.md").write_text(attribution(items), encoding="utf-8", newline="\n")
    if args.zip:
        print(f"{args.zip}: sha256 {pack(args.out, items, args.zip)}")


if __name__ == "__main__":
    sys.exit(main())
