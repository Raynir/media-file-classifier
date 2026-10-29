# Domain model

## Storage cost of a previous run

D020 keeps one previous analysis run per item and analyzer by default (`LibrarySettings.RetainedPreviousRuns`). This estimates what that costs for a 100k-item library.

A previous run holds the same kind of data as the active run, so it costs about as much as the active AI layer.

**Assumptions** (per item, SQLite):

- Each stored row costs about 110 bytes besides its text: run ID, item ID and model ID (16 bytes each), field, confidence and date, the record header, and one secondary-index entry.
- The caption analyzer writes 8 fields: title (60 B), description (400 B), summary (150 B), and five short lists (objects, scenes, actions, emotions, people: 300 B together).
- A quarter of the items have OCR text (400 B on average), and a tenth are videos with a transcript (3 KB on average).
- An item gets about 15 AI tags of about 12 bytes each.

| Part | Rows per item | Bytes per item |
|------|---------------|----------------|
| Run rows (caption, plus OCR and transcript where they apply) | ~2 | ~350 |
| Caption field values | 8 | ~1,800 |
| OCR text | 0.25 | ~130 |
| Transcript | 0.1 | ~310 |
| AI tag assignments | 15 | ~1,800 |
| **Total** | | **~4.4 KB** |

**Estimate:** about **0.45 GB** for 100k items, in a range from about 0.2 GB (short descriptions, few tags) to about 0.8 GB (many videos with long transcripts).

For comparison, at 100k items the active AI layer is also about 0.45 GB, 512-dimension float embeddings are about 0.2 GB, and thumbnails at about 20 KB each are about 2 GB. Keeping one previous run adds roughly 15–20% to a typical library.

**Conclusion:** the default of one previous run is affordable. If it turns out too large, the persistence layer (E01-S03) can:

- leave the model ID out of value rows, since the run already records it (about 0.35 KB per item),
- store a previous run's values and tags as one compact JSON row per run, instead of a row each,
- leave previous runs out of the full-text index, since search uses only effective values.

Users can also set the retention to 0.
