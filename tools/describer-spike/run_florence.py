"""Runs Florence-2 (onnx-community ONNX export) in-process over the spike samples.

  run_florence.py base|large [--precision fp32|int8] [--ep cpu|dml|hybrid] [--threads N] [--every K]

Per sample it computes the image features once and runs three task prompts, each a greedy decode with the generation
config's no-repeat-3-gram rule (the reference uses beam search with 3 beams, about 3 times the decoder work):
<MORE_DETAILED_CAPTION> -> description, <CAPTION> -> summary, <OD> -> objects (labels, boxes dropped) and tags.
Florence-2 writes no title, Russian forms, scenes, actions or emotions; those stay empty.
Writes eval/describer-spike/results/florence2-<size>-<precision>-<ep>.json after every item.
"""

import argparse
import re
import subprocess
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import psutil
from PIL import Image
from tokenizers import Tokenizer

from common import ROOT, input_path, load_result, load_samples, save_result

TASKS = {
    "description": ("<MORE_DETAILED_CAPTION>", "Describe with a paragraph what is shown in the image.", 256),
    "summary": ("<CAPTION>", "What does the image describe?", 64),
    "objects": ("<OD>", "Locate the objects with category name in the image.", 512),
}
MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)
SIZE = 768
START, BOS, EOS = 2, 0, 2
NO_REPEAT = 3
LOCATION = re.compile(r"([^<>]+?)((?:<loc_\d+>){4})")


def gpu_used_mib():
    out = subprocess.check_output(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"], text=True)
    return int(out.strip().splitlines()[0])


class Florence:
    def __init__(self, folder: Path, precision, ep, threads):
        suffix = "" if precision == "fp32" else "_int8"
        options = ort.SessionOptions()
        if threads:
            options.intra_op_num_threads = threads
        gpu = ["DmlExecutionProvider", "CPUExecutionProvider"]
        cpu = ["CPUExecutionProvider"]
        if ep != "cpu":
            options.enable_mem_pattern = False
            options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL

        def session(name, providers):
            return ort.InferenceSession(str(folder / "onnx" / f"{name}{suffix}.onnx"), options, providers=providers)

        # "hybrid": the merged decoder returns garbage tokens on DirectML (ORT 1.24, every optimisation level; the
        # encoder output matches the CPU's), so it and the token embedding stay on the CPU, the encoders on the GPU.
        self.vision = session("vision_encoder", cpu if ep == "cpu" else gpu)
        self.embed = session("embed_tokens", gpu if ep == "dml" else cpu)
        self.encoder = session("encoder_model", cpu if ep == "cpu" else gpu)
        self.decoder = session("decoder_model_merged", gpu if ep == "dml" else cpu)
        self.tokenizer = Tokenizer.from_file(str(folder / "tokenizer.json"))
        self.past_names = [i.name for i in self.decoder.get_inputs() if i.name.startswith("past_key_values")]
        shape = next(i.shape for i in self.decoder.get_inputs() if i.name.startswith("past_key_values"))
        self.heads, self.head_dim = shape[1], shape[3]

    def image_features(self, path):
        image = Image.open(path).convert("RGB").resize((SIZE, SIZE), Image.Resampling.BICUBIC)
        pixels = (np.asarray(image, dtype=np.float32) / 255.0 - MEAN) / STD
        return self.vision.run(None, {"pixel_values": pixels.transpose(2, 0, 1)[None].astype(np.float32)})[0]

    def embed_ids(self, ids):
        return self.embed.run(None, {"input_ids": np.array([ids], dtype=np.int64)})[0]

    def run_task(self, features, prompt, max_tokens):
        ids = self.tokenizer.encode(prompt).ids
        inputs = np.concatenate([features, self.embed_ids(ids)], axis=1)
        mask = np.ones(inputs.shape[:2], dtype=np.int64)
        hidden = self.encoder.run(None, {"inputs_embeds": inputs, "attention_mask": mask})[0]

        tokens = [START]
        empty = np.zeros((1, self.heads, 0, self.head_dim), dtype=np.float32)
        past = {name: empty for name in self.past_names}
        use_cache = False
        output_names = [o.name for o in self.decoder.get_outputs()]
        for step in range(max_tokens):
            feed = {
                "encoder_attention_mask": mask,
                "encoder_hidden_states": hidden,
                "inputs_embeds": self.embed_ids(tokens if not use_cache else tokens[-1:]),
                "use_cache_branch": np.array([use_cache]),
                **past,
            }
            outputs = dict(zip(output_names, self.decoder.run(None, feed)))
            logits = outputs["logits"][0, -1].copy()
            if step == 0:
                next_token = BOS  # forced_bos_token_id
            else:
                for banned in self.banned(tokens):
                    logits[banned] = -np.inf
                next_token = int(np.argmax(logits))
            new_past = {}
            for name in self.past_names:
                present = outputs[name.replace("past_key_values", "present")]
                # Once the cache is used, the merged model returns no new encoder keys; keep the first ones.
                new_past[name] = past[name] if (use_cache and ".encoder." in name) else present
            past = new_past
            use_cache = True
            tokens.append(next_token)
            if next_token == EOS:
                break
        return tokens[1:]

    @staticmethod
    def banned(tokens):
        if len(tokens) < NO_REPEAT:
            return []
        prefix = tuple(tokens[-(NO_REPEAT - 1):])
        return [tokens[i + NO_REPEAT - 1] for i in range(len(tokens) - NO_REPEAT + 1) if tuple(tokens[i:i + NO_REPEAT - 1]) == prefix]

    def decode(self, tokens, keep_special=False):
        return self.tokenizer.decode(tokens, skip_special_tokens=not keep_special)


def object_labels(text):
    text = text.replace("<s>", "").replace("</s>", "").replace("<pad>", "")
    labels = []
    for label, _ in LOCATION.findall(text):
        label = label.strip()
        if label and label.lower() not in (existing.lower() for existing in labels):
            labels.append(label)
    return labels


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("size", choices=["base", "large"])
    parser.add_argument("--precision", choices=["fp32", "int8"], default="fp32")
    parser.add_argument("--ep", choices=["cpu", "dml", "hybrid"], default="cpu")
    parser.add_argument("--threads", type=int, default=0)
    parser.add_argument("--every", type=int, default=1)
    args = parser.parse_args()

    system = f"florence2-{args.size}-{args.precision}-{args.ep}" + (f"-{args.threads}t" if args.threads else "")
    samples = load_samples()[::args.every]
    gpu_before = gpu_used_mib()
    started = time.perf_counter()
    model = Florence(ROOT / "models" / f"florence2-{args.size}", args.precision, args.ep, args.threads)
    load_seconds = time.perf_counter() - started
    result = {"system": system, "route": "in-process", "model": f"Florence-2-{args.size}-ft", "precision": args.precision,
              "device": args.ep, "threads": args.threads or "all", "promptVersion": "florence-tasks-1", "items": {}}

    warm = model.image_features(input_path(samples[0]["id"]))  # warm-up, not timed
    model.run_task(warm, TASKS["summary"][1], 8)

    for i, sample in enumerate(samples):
        timings = {}
        t = time.perf_counter()
        features = model.image_features(input_path(sample["id"]))
        timings["vision"] = time.perf_counter() - t
        texts = {}
        for field, (_, prompt, max_tokens) in TASKS.items():
            t = time.perf_counter()
            tokens = model.run_task(features, prompt, max_tokens)
            timings[field] = time.perf_counter() - t
            texts[field] = model.decode(tokens, keep_special=field == "objects")
        objects = object_labels(texts["objects"])
        output = {
            "title": None, "title_ru": None,
            "description": texts["description"].strip(), "summary": texts["summary"].strip(),
            "tags": [{"en": label.lower(), "ru": None} for label in objects[:15]],
            "objects": objects, "scenes": [], "actions": [], "emotions": [],
        }
        result["items"][sample["id"]] = {
            "seconds": round(sum(timings.values()), 2),
            "timings": {key: round(value, 3) for key, value in timings.items()},
            "firstTryValid": True, "output": output,
        }
        print(f"  {i + 1}/{len(samples)} {sample['id']} {sum(timings.values()):.2f} s")
        save_result(system, result)

    process = psutil.Process()
    result["load"] = {"loadSeconds": round(load_seconds, 1),
                      "peakWorkingSetMiB": round(process.memory_info().peak_wset / 2**20),
                      "gpuModelMiB": gpu_used_mib() - gpu_before if args.ep != "cpu" else None}
    save_result(system, result)
    print(result["load"])


if __name__ == "__main__":
    main()
