"""Runs one LM Studio vision-language model over the spike samples with the draft prompt (prompt.py).

  run_vlm.py <model key> [--prompt a|b] [--device gpu|cpu] [--limit N] [--every K]

Loads the model with `lms load` (GPU: full offload; CPU: no offload), sends each sample like the app's
OpenAiCompatibleImageDescriber (schema as response_format, temperature 0.2, presence penalty 1.5, one retry with the
reason), and writes eval/describer-spike/results/<model>[-cpu].json after every item, so a stopped run resumes. Times are
wall-clock per request after one warm-up request; GPU memory is nvidia-smi's used memory before and after loading.
"""

import argparse
import base64
import json
import subprocess
import time
import urllib.request

import prompt
from common import input_path, load_result, load_samples, media_context, save_result

ENDPOINT = "http://localhost:1234/v1/chat/completions"
CONTEXT_LENGTH = 8192


def gpu_used_mib():
    out = subprocess.check_output(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"], text=True)
    return int(out.strip().splitlines()[0])


def lms(*args):
    return subprocess.run(["lms", *args], capture_output=True, text=True, check=False)


def load_model(model, device):
    lms("unload", "--all")
    time.sleep(3)
    before = gpu_used_mib()
    gpu = "max" if device == "gpu" else "off"
    result = lms("load", model, "--gpu", gpu, "--context-length", str(CONTEXT_LENGTH), "-y")
    if result.returncode != 0:
        raise SystemExit(f"lms load failed: {result.stdout}\n{result.stderr}")
    time.sleep(3)
    after = gpu_used_mib()
    ps = lms("ps", "--json")
    size = None
    try:
        size = next((m.get("sizeBytes") for m in json.loads(ps.stdout) if m.get("modelKey") == model or m.get("identifier") == model), None)
    except json.JSONDecodeError:
        pass
    return {"gpuBeforeMiB": before, "gpuAfterMiB": after, "gpuModelMiB": after - before, "modelBytes": size}


def chat(model, messages, variant="a"):
    body = {
        "model": model,
        "messages": messages,
        "temperature": prompt.TEMPERATURE,
        "presence_penalty": prompt.PRESENCE_PENALTY,
        "max_tokens": prompt.MAX_OUTPUT_TOKENS,
        "response_format": {"type": "json_schema", "json_schema": {"name": "image_description", "schema": prompt.schema(variant)}},
    }
    request = urllib.request.Request(ENDPOINT, data=json.dumps(body).encode("utf-8"), headers={"Content-Type": "application/json"})
    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=900) as response:
        answer = json.loads(response.read().decode("utf-8"))
    seconds = time.perf_counter() - started
    message = answer["choices"][0]["message"]
    return message.get("content") or "", seconds, answer.get("usage", {}), answer["choices"][0].get("finish_reason")


def describe(model, sample, variant="a"):
    image = base64.b64encode(input_path(sample["id"]).read_bytes()).decode("ascii")
    messages = [
        {"role": "system", "content": prompt.SYSTEM},
        {"role": "user", "content": [
            {"type": "text", "text": prompt.user_prompt(media_context(sample), variant)},
            {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{image}"}},
        ]},
    ]
    raw, seconds, usage, finish = chat(model, messages, variant)
    parsed, error = prompt.validate(raw)
    record = {"seconds": round(seconds, 2), "outputTokens": usage.get("completion_tokens"), "finish": finish,
              "firstTryValid": parsed is not None, "firstTryError": error, "raw": raw}
    if parsed is None:
        messages += [{"role": "assistant", "content": raw},
                     {"role": "user", "content": f"That answer can't be used: {error}. Answer again with only the JSON object, with every field filled in."}]
        raw, retry_seconds, usage, finish = chat(model, messages, variant)
        parsed, error = prompt.validate(raw)
        record.update({"retrySeconds": round(retry_seconds, 2), "retryError": error, "retryRaw": raw})
    record["output"] = parsed
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("model")
    parser.add_argument("--device", choices=["gpu", "cpu"], default="gpu")
    parser.add_argument("--limit", type=int)
    parser.add_argument("--prompt", choices=["a", "b"], default="a")
    parser.add_argument("--every", type=int, default=1, help="take every K-th sample (CPU timing subset)")
    args = parser.parse_args()

    samples = load_samples()[::args.every][:args.limit]
    system = args.model + ("-b" if args.prompt == "b" else "") + ("-cpu" if args.device == "cpu" else "")
    result = load_result(system) or {"system": system, "route": "lmstudio", "model": args.model, "device": args.device,
                                      "promptVersion": prompt.VARIANTS[args.prompt], "items": {}}
    result["load"] = load_model(args.model, args.device)
    print(f"{system}: loaded, {result['load']}")

    describe(args.model, samples[0], args.prompt)  # warm-up, not timed
    for i, sample in enumerate(samples):
        if sample["id"] in result["items"]:
            continue
        try:
            record = describe(args.model, sample, args.prompt)
        except Exception as error:  # noqa: BLE001 - spike: record and go on
            record = {"error": str(error), "output": None}
        result["items"][sample["id"]] = record
        save_result(system, result)
        print(f"  {i + 1}/{len(samples)} {sample['id']} {record.get('seconds')} s valid={record.get('firstTryValid')} {record.get('firstTryError') or ''}")
    lms("unload", "--all")


if __name__ == "__main__":
    main()
