"""The candidate image-text models of MFC-53, behind one interface.

Each encoder turns PIL images and query strings into L2-normalized float32 vectors.
Quality runs use the reference PyTorch implementation, or the publisher's ONNX export
where the model only ships with custom code (mexma-siglip2, jina-clip-v2).
OnnxTowerEncoder runs the towers written by export_onnx.py with the same preprocessing,
to check the exported and quantized graphs.
"""

import os
from collections.abc import Callable
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from huggingface_hub import hf_hub_download
from PIL import Image

DEVICE = os.environ.get("MFC_SPIKE_DEVICE") or ("cuda" if torch.cuda.is_available() else "cpu")


def _normalize(vectors: np.ndarray) -> np.ndarray:
    vectors = vectors.astype(np.float32)
    return vectors / np.linalg.norm(vectors, axis=-1, keepdims=True)


def _features(output) -> torch.Tensor:
    """get_*_features returns a tensor in older transformers and a model output in newer ones."""
    if isinstance(output, torch.Tensor):
        return output
    return output.pooler_output


def ort_session(path: Path | str, providers: list[str], threads: int = 0):
    import onnxruntime as ort

    ort.preload_dlls()
    options = ort.SessionOptions()
    options.intra_op_num_threads = threads
    options.log_severity_level = 3
    return ort.InferenceSession(str(path), options, providers=providers)


class Encoder:
    name: str

    def prepare_images(self, images: list[Image.Image]) -> np.ndarray:
        """Pixel values as the image tower takes them (N, 3, H, W)."""
        raise NotImplementedError

    def prepare_texts(self, texts: list[str]) -> dict[str, np.ndarray]:
        """Token tensors keyed by the text tower's input names."""
        raise NotImplementedError

    def encode_images(self, images: list[Image.Image]) -> np.ndarray:
        raise NotImplementedError

    def encode_texts(self, texts: list[str]) -> np.ndarray:
        raise NotImplementedError


class TorchEncoder(Encoder):
    model: torch.nn.Module

    def image_features(self, pixels: torch.Tensor) -> torch.Tensor:
        raise NotImplementedError

    def text_features(self, input_ids: torch.Tensor, attention_mask: torch.Tensor | None) -> torch.Tensor:
        raise NotImplementedError

    @torch.inference_mode()
    def encode_images(self, images):
        pixels = torch.from_numpy(self.prepare_images(images)).to(DEVICE)
        return F.normalize(self.image_features(pixels), dim=-1).cpu().numpy()

    @torch.inference_mode()
    def encode_texts(self, texts):
        tokens = {k: torch.from_numpy(v).to(DEVICE) for k, v in self.prepare_texts(texts).items()}
        return F.normalize(self.text_features(tokens["input_ids"], tokens.get("attention_mask")), dim=-1).cpu().numpy()


class TransformersEncoder(TorchEncoder):
    """SigLIP 2 and MetaCLIP 2 through transformers' AutoModel."""

    def __init__(self, name: str, repo: str, text_kwargs: dict):
        from transformers import AutoModel, AutoProcessor

        self.name = name
        self.model = AutoModel.from_pretrained(repo, dtype=torch.float32).to(DEVICE).eval()
        self.processor = AutoProcessor.from_pretrained(repo)
        self.text_kwargs = text_kwargs

    def prepare_images(self, images):
        return self.processor(images=images, return_tensors="np")["pixel_values"].astype(np.float32)

    def prepare_texts(self, texts):
        tokens = self.processor(text=texts, return_tensors="np", truncation=True, **self.text_kwargs)
        return {k: tokens[k].astype(np.int64) for k in ("input_ids", "attention_mask") if k in tokens}

    def image_features(self, pixels):
        return _features(self.model.get_image_features(pixel_values=pixels))

    def text_features(self, input_ids, attention_mask):
        kwargs = {} if attention_mask is None else {"attention_mask": attention_mask}
        return _features(self.model.get_text_features(input_ids=input_ids, **kwargs))


class OpenClipEncoder(TorchEncoder):
    """OpenCLIP models: the XLM-R multilingual one and the English OpenAI baselines."""

    def __init__(self, name: str, architecture: str, pretrained: str):
        import open_clip

        self.name = name
        self.model, _, self.preprocess = open_clip.create_model_and_transforms(architecture, pretrained=pretrained)
        self.model = self.model.to(DEVICE).eval()
        self.tokenizer = open_clip.get_tokenizer(architecture)

    def prepare_images(self, images):
        return torch.stack([self.preprocess(image) for image in images]).numpy()

    def prepare_texts(self, texts):
        return {"input_ids": self.tokenizer(texts).numpy().astype(np.int64)}

    def image_features(self, pixels):
        return self.model.encode_image(pixels)

    def text_features(self, input_ids, attention_mask):
        return self.model.encode_text(input_ids)


class MultilingualDistilledEncoder(OpenClipEncoder):
    """sentence-transformers' multilingual DistilBERT, distilled into OpenAI CLIP ViT-B/32's space."""

    def __init__(self):
        from sentence_transformers import SentenceTransformer

        super().__init__("st-clip-vit-b32-multilingual-v1", "ViT-B-32", "openai")
        self.text_model = SentenceTransformer("sentence-transformers/clip-ViT-B-32-multilingual-v1", device=DEVICE)

    def prepare_texts(self, texts):
        tokens = self.text_model.tokenizer(texts, padding=True, truncation=True, max_length=128, return_tensors="np")
        return {k: tokens[k].astype(np.int64) for k in ("input_ids", "attention_mask")}

    def text_features(self, input_ids, attention_mask):
        return self.text_model({"input_ids": input_ids, "attention_mask": attention_mask})["sentence_embedding"]


class OnnxEncoder(Encoder):
    """Base for the publisher ONNX exports, run with ONNX Runtime on CUDA."""

    providers = ["CUDAExecutionProvider", "CPUExecutionProvider"]

    @staticmethod
    def _pixels(images, size: int, mean, std, crop: bool) -> np.ndarray:
        arrays = []
        for image in images:
            if crop:
                scale = size / min(image.size)
                resized = image.resize((round(image.width * scale), round(image.height * scale)), Image.BICUBIC)
                left, top = (resized.width - size) // 2, (resized.height - size) // 2
                resized = resized.crop((left, top, left + size, top + size))
            else:
                resized = image.resize((size, size), Image.BILINEAR)
            arrays.append((np.asarray(resized, dtype=np.float32) / 255.0 - mean) / std)
        return np.stack(arrays).transpose(0, 3, 1, 2).astype(np.float32)


class MexmaSiglip2Encoder(OnnxEncoder):
    """MEXMA (XLM-R large) text encoder with SigLIP 2 so400m-patch16-512 image encoder.

    The published ONNX towers are fixed to one input per call, and the text tower to 32 tokens.
    """

    repo = "visheratin/mexma-siglip2"

    def __init__(self, sessions: bool = True):
        from transformers import AutoTokenizer

        self.name = "mexma-siglip2"
        self.tokenizer = AutoTokenizer.from_pretrained(self.repo)
        if sessions:
            self.image_session = ort_session(hf_hub_download(self.repo, "mexma_image_encoder.onnx"), self.providers)
            self.text_session = ort_session(hf_hub_download(self.repo, "mexma_text_encoder.onnx"), self.providers)

    def prepare_images(self, images):
        return self._pixels(images, 512, 0.5, 0.5, crop=False)

    def prepare_texts(self, texts):
        tokens = self.tokenizer(texts, padding="max_length", truncation=True, max_length=32, return_tensors="np")
        return {k: tokens[k].astype(np.int64) for k in ("input_ids", "attention_mask")}

    def encode_images(self, images):
        pixels = self.prepare_images(images)
        return _normalize(np.concatenate([self.image_session.run(None, {"pixel_values": pixels[i:i + 1]})[0] for i in range(len(pixels))]))

    def encode_texts(self, texts):
        tokens = self.prepare_texts(texts)
        return _normalize(np.concatenate([
            self.text_session.run(None, {k: v[i:i + 1] for k, v in tokens.items()})[0] for i in range(len(texts))]))


class JinaClipV2Encoder(OnnxEncoder):
    """jina-clip-v2 through the publisher's ONNX graph, split into its two towers by export_onnx.py.

    The published graph takes a text and an image batch of the same size in every call, so
    running it whole would encode a dummy image for every query.
    """

    repo = "jinaai/jina-clip-v2"
    mean = np.array([0.48145466, 0.4578275, 0.40821073], dtype=np.float32)
    std = np.array([0.26862954, 0.26130258, 0.27577711], dtype=np.float32)

    def __init__(self, sessions: bool = True):
        from transformers import AutoTokenizer

        self.name = "jina-clip-v2"
        self.tokenizer = AutoTokenizer.from_pretrained(self.repo)
        if sessions:
            folder = Path(__file__).resolve().parents[2] / "models" / "onnx" / "jina-clip-v2"
            self.image_session = ort_session(folder / "image.onnx", self.providers)
            self.text_session = ort_session(folder / "text.onnx", self.providers)

    def prepare_images(self, images):
        return self._pixels(images, 512, self.mean, self.std, crop=True)

    def prepare_texts(self, texts):
        tokens = self.tokenizer(texts, padding=True, truncation=True, max_length=512, return_tensors="np")
        return {"input_ids": tokens["input_ids"].astype(np.int64)}

    def encode_images(self, images):
        return _normalize(self.image_session.run(None, {"pixel_values": self.prepare_images(images)})[0])

    def encode_texts(self, texts):
        return _normalize(self.text_session.run(None, self.prepare_texts(texts))[0])


class OnnxTowerEncoder(Encoder):
    """Runs exported towers (models/onnx/<model>/{image,text}[_int8].onnx) with the base model's preprocessing."""

    def __init__(self, base: Encoder, folder: Path, image_variant: str, text_variant: str, providers: list[str]):
        self.base = base
        self.name = f"{base.name}-onnx-image-{image_variant}-text-{text_variant}"
        self.image_session = ort_session(folder / f"image{'' if image_variant == 'fp32' else '_' + image_variant}.onnx", providers)
        self.text_session = ort_session(folder / f"text{'' if text_variant == 'fp32' else '_' + text_variant}.onnx", providers)

    def encode_images(self, images):
        name = self.image_session.get_inputs()[0].name
        return _normalize(self.image_session.run(None, {name: self.base.prepare_images(images)})[0])

    def encode_texts(self, texts):
        tokens = self.base.prepare_texts(texts)
        return _normalize(self.text_session.run(None, {i.name: tokens[i.name] for i in self.text_session.get_inputs()})[0])


SIGLIP_TEXT = {"padding": "max_length", "max_length": 64}
CLIP_TEXT = {"padding": True, "max_length": 77}

ENCODERS: dict[str, Callable[..., Encoder]] = {
    "siglip2-base-patch16-224": lambda: TransformersEncoder("siglip2-base-patch16-224", "google/siglip2-base-patch16-224", SIGLIP_TEXT),
    "siglip2-base-patch16-256": lambda: TransformersEncoder("siglip2-base-patch16-256", "google/siglip2-base-patch16-256", SIGLIP_TEXT),
    "siglip2-base-patch16-384": lambda: TransformersEncoder("siglip2-base-patch16-384", "google/siglip2-base-patch16-384", SIGLIP_TEXT),
    "siglip2-so400m-patch16-256": lambda: TransformersEncoder("siglip2-so400m-patch16-256", "google/siglip2-so400m-patch16-256", SIGLIP_TEXT),
    "metaclip2-worldwide-s16": lambda: TransformersEncoder("metaclip2-worldwide-s16", "facebook/metaclip-2-worldwide-s16", CLIP_TEXT),
    "metaclip2-mt5-worldwide-s16": lambda: TransformersEncoder("metaclip2-mt5-worldwide-s16", "facebook/metaclip-2-mt5-worldwide-s16", CLIP_TEXT),
    "openclip-xlm-roberta-base-vit-b32": lambda: OpenClipEncoder("openclip-xlm-roberta-base-vit-b32", "xlm-roberta-base-ViT-B-32", "laion5b_s13b_b90k"),
    "st-clip-vit-b32-multilingual-v1": MultilingualDistilledEncoder,
    "mexma-siglip2": MexmaSiglip2Encoder,
    "jina-clip-v2": JinaClipV2Encoder,
    "openai-clip-vit-b16": lambda: OpenClipEncoder("openai-clip-vit-b16", "ViT-B-16", "openai"),
}
