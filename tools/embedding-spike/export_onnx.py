"""Exports a candidate's image and text towers to ONNX, plus int8 (dynamic) copies.

Writes models/onnx/<model>/image.onnx, text.onnx, image_int8.onnx and text_int8.onnx.
PyTorch models are exported with the TorchScript exporter. mexma-siglip2 already ships
separate towers, and jina-clip-v2 ships one graph that is split into its two towers.

Usage (from the repository root, with the spike's virtual environment):
    python tools/embedding-spike/export_onnx.py siglip2-base-patch16-256
"""

import argparse
import os
import shutil

os.environ.setdefault("MFC_SPIKE_DEVICE", "cpu")

import onnx  # noqa: E402
import torch  # noqa: E402
from huggingface_hub import hf_hub_download  # noqa: E402
from onnxruntime.quantization import QuantType, quantize_dynamic  # noqa: E402
from PIL import Image  # noqa: E402

from corpus import REPO_ROOT  # noqa: E402
from encoders import ENCODERS, JinaClipV2Encoder, MexmaSiglip2Encoder, TorchEncoder  # noqa: E402

ONNX_DIR = REPO_ROOT / "models" / "onnx"
SAMPLE_TEXTS = ["a cat on a sofa", "кошка на диване"]


class ImageTower(torch.nn.Module):
    def __init__(self, encoder: TorchEncoder):
        super().__init__()
        self.encoder = encoder
        self.model = encoder.model

    def forward(self, pixel_values):
        return self.encoder.image_features(pixel_values)


class TextTower(torch.nn.Module):
    def __init__(self, encoder: TorchEncoder):
        super().__init__()
        self.encoder = encoder
        self.model = encoder.model
        self.text_model = getattr(encoder, "text_model", None)

    def forward(self, input_ids, attention_mask=None):
        return self.encoder.text_features(input_ids, attention_mask)


def export_torch(encoder: TorchEncoder, target):
    image = Image.new("RGB", (640, 480), (120, 160, 200))
    pixels = torch.from_numpy(encoder.prepare_images([image, image]))
    tokens = encoder.prepare_texts(SAMPLE_TEXTS)
    names = list(tokens)
    torch.onnx.export(
        ImageTower(encoder).eval(), (pixels,), target / "image.onnx", dynamo=False, opset_version=17,
        input_names=["pixel_values"], output_names=["image_embeds"],
        dynamic_axes={"pixel_values": {0: "batch"}, "image_embeds": {0: "batch"}})
    torch.onnx.export(
        TextTower(encoder).eval(), tuple(torch.from_numpy(tokens[n]) for n in names), target / "text.onnx",
        dynamo=False, opset_version=17, input_names=names, output_names=["text_embeds"],
        dynamic_axes={**{n: {0: "batch", 1: "sequence"} for n in names}, "text_embeds": {0: "batch"}})


def split_jina(target):
    repo = JinaClipV2Encoder.repo
    hf_hub_download(repo, "onnx/model.onnx_data")
    model = onnx.load(hf_hub_download(repo, "onnx/model.onnx"))
    outputs = [o.name for o in model.graph.output]
    extractor = onnx.utils.Extractor(model)
    # The text tower is over the 2 GB protobuf limit, so the weights go to a side file.
    for tower, inputs, output in (("text", ["input_ids"], outputs[2]), ("image", ["pixel_values"], outputs[3])):
        onnx.save(extractor.extract_model(inputs, [output]), target / f"{tower}.onnx", save_as_external_data=True, location=f"{tower}.onnx.data")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", choices=sorted(ENCODERS))
    args = parser.parse_args()
    target = ONNX_DIR / args.model
    target.mkdir(parents=True, exist_ok=True)

    if args.model == "mexma-siglip2":
        shutil.copy(hf_hub_download(MexmaSiglip2Encoder.repo, "mexma_image_encoder.onnx"), target / "image.onnx")
        shutil.copy(hf_hub_download(MexmaSiglip2Encoder.repo, "mexma_text_encoder.onnx"), target / "text.onnx")
    elif args.model == "jina-clip-v2":
        split_jina(target)
    else:
        encoder = ENCODERS[args.model]()
        # The fused attention fast path of nn.MultiheadAttention (OpenCLIP) has no ONNX export.
        torch.backends.mha.set_fastpath_enabled(False)
        with torch.no_grad():
            export_torch(encoder, target)

    for tower in ("image", "text"):
        quantize_dynamic(target / f"{tower}.onnx", target / f"{tower}_int8.onnx", weight_type=QuantType.QInt8, use_external_data_format=args.model == "jina-clip-v2")
    for file in sorted(target.iterdir()):
        print(f"{file.name}: {file.stat().st_size / 2**20:.0f} MB")


if __name__ == "__main__":
    main()
