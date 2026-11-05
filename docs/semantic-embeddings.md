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

## Query vectors (T02, T04)

`SemanticQueryEncoder` is a singleton that follows `ILibrarySession`:

- **Text:** `EncodeTextAsync(text)` lower-cases and trims the text, and returns a `SearchVector` in the open library's model registration. It keeps 256 vectors (least recently used out first), keyed by model and text. Median latency after preload is about 23 ms (budget 50 ms, D031).
- **Image:** `EncodeImageAsync(AiImage)` and `EncodeImageFileAsync(path)` embed a picture that isn't in the library (a dropped or pasted file). They add nothing to the library's items, and nothing is cached. The vector goes into `SearchQuery.Vector` for image-to-image search.
- **Lifecycle:** when a library opens, the text tower loads in the background (a missing model is logged, not thrown). When it closes, the model is unloaded and the cache cleared.
- **No library or no provider:** both methods return `null`. A model that isn't installed throws `AiProviderException` with `ModelMissing`, and the search falls back to text matching (D037).
