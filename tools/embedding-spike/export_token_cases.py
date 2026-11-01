"""Writes the reference token IDs of every evaluation query for TokenizerCheck.cs (E06-S03-T03).

Each tokenizer family gets its SentencePiece model file and, per query, the IDs the Python
reference produces, so the .NET check can compare Microsoft.ML.Tokenizers against them.

Usage (from the repository root, with the spike's virtual environment):
    python tools/embedding-spike/export_token_cases.py
    dotnet run tools/embedding-spike/TokenizerCheck.cs
"""

import json

from huggingface_hub import hf_hub_download
from transformers import AutoProcessor, AutoTokenizer

from corpus import REPO_ROOT, load_queries


def main() -> None:
    texts = [q.text for q in load_queries()]
    siglip = AutoProcessor.from_pretrained("google/siglip2-base-patch16-256")
    families = {
        # SigLIP 2 expects lower-cased text, padded to 64 tokens.
        "siglip2": ("google/siglip2-base-patch16-256", "tokenizer.model",
                    lambda text: list(siglip(text=[text.lower()], padding="max_length", max_length=64, truncation=True)["input_ids"][0])),
    }
    for family, repo, file in (("xlmr", "xlm-roberta-base", "sentencepiece.bpe.model"),
                               ("xlmv", "facebook/metaclip-2-worldwide-s16", "sentencepiece.bpe.model"),
                               ("mt5", "facebook/metaclip-2-mt5-worldwide-s16", "spiece.model")):
        tokenizer = AutoTokenizer.from_pretrained(repo)
        families[family] = (repo, file, lambda text, t=tokenizer: t(text)["input_ids"])

    cases = {}
    for family, (repo, file, encode) in families.items():
        cases[family] = {
            "model": hf_hub_download(repo, file),
            "cases": [{"text": text.lower() if family == "siglip2" else text, "ids": [int(i) for i in encode(text)]} for text in texts],
        }
    target = REPO_ROOT / "models" / "token-cases.json"
    target.write_text(json.dumps(cases, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {target}")


if __name__ == "__main__":
    main()
