# Semantic embeddings (E06-S03)

SigLIP 2 base-patch16-256 (`siglip2-base-patch16-256`, version `1.0`) runs locally through ONNX Runtime. It maps images and text into one 768-dimension space, so a text query finds pictures by similarity (D031).

## Pieces

| Piece | Project | What it does |
|---|---|---|
| `Siglip2EmbeddingProvider` | `Infrastructure.AI/Embeddings` | Image and text embedding, lazy model lookup in the model store, `Unload()` before a model delete. |
| `Siglip2ImagePreprocessor`, `PilBilinearResampler` | same | Decode, composite over white, orientation, resize to 256 × 256 exactly like Pillow's bilinear. |
| `Siglip2Tokenizer` | same | The tokenizer described below. |
| `ItemEmbeddingAnalyzer` | `UseCases/Embeddings` | Stores an item's vectors (a still: one; a video or animation: per keyframe and their mean, D228). |
| `SemanticQueryEncoder` | `UseCases/Embeddings` | Query vectors for search: text (cached) and image (no cache). |

## Tokenizer (T03)

The text tower needs the Gemma SentencePiece vocabulary (`tokenizer.model`, a role of the model in the catalogue). `Siglip2Tokenizer` runs it through `Microsoft.ML.Tokenizers`:

- the text is lower-cased first, because SigLIP 2 was trained on lower-case text;
- the result is truncated to 63 pieces, then `</s>` (id 1), then padded with 0 to 64 ids;
- there is no beginning-of-sentence token.

`Siglip2TokenizerTests` compares the ids with the Hugging Face tokenizer for the 215 English and Russian queries of the evaluation set (`eval/queries.json`, D085). They must be identical. The file isn't committed, so the test skips where `models/siglip2/tokenizer.model` is missing (CI). To regenerate the reference, see `tools/embedding-reference/README.md`.
