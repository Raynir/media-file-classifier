"""Generates the video of the visual near-duplicate test set in tests/SampleMedia/near-duplicates (E12-S02).

The test set itself is features.json: perceptual hashes and SigLIP 2 vectors of evaluation corpus photos, copies
made of them, and sample videos. NearDuplicateVectorCapture writes it with the model (see the SampleMedia README).
This script only makes the re-encoded copy of video-short.mp4 that the capture reads. The output is committed, so
CI doesn't need this script.

Requirements: Python 3 and ffmpeg on PATH.

Usage (from the repository root):
    python tools/sample-media/generate_near_duplicate_set.py tests/SampleMedia/near-duplicates
"""

import argparse
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", type=Path, help="Folder to write the video to.")
    args = parser.parse_args()
    out: Path = args.output
    out.mkdir(parents=True, exist_ok=True)

    # A re-encoded copy of the short sample video: smaller, stronger compression, no metadata (E12-S02-T02).
    source = Path(__file__).resolve().parents[2] / "tests" / "SampleMedia" / "video-short.mp4"
    target = out / "video-short-reencoded.mp4"
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(source), "-c:v", "libx264", "-crf", "34", "-preset", "medium",
         "-vf", "scale=256:192", "-c:a", "aac", "-b:a", "64k", "-map_metadata", "-1", "-fflags", "+bitexact",
         "-flags:v", "+bitexact", "-flags:a", "+bitexact", str(target)],
        check=True)
    print(f"{target.name}: {target.stat().st_size} bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
