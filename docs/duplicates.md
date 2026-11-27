# Duplicate detection

How duplicate groups are found (E12). Storage, triggers and queues are in [library-storage.md](library-storage.md).

## Exact duplicates (E12-S01)

Items that share a full content hash form an exact group (D575–D578).

## Visual near-duplicates (E12-S02)

The same picture saved differently: re-encoded, resized, converted, a thumbnail, or at the loose sensitivity also cropped, edited or shot in a burst.

### Matching

`VisualMatchRules` decides whether two items match. A match is one of:

| Tier | Perceptual hash distance | SigLIP 2 cosine similarity | Strict | Loose |
|---|---|---|---|---|
| Close | ≤ 4 bits | ≥ 0.80, or either item has no embedding yet | yes | yes |
| Wide | ≤ 16 bits | ≥ 0.90 | no | yes |
| Embedding | any | ≥ 0.95 | no | yes |

- **Stills** compare their perceptual hash. **Videos and animated images** compare keyframe hashes: at least half of one item's keyframes need a keyframe of the other within the distance. Their durations must differ by at most 0.5 s or 2 %. Stills never match videos or animations.
- **Embeddings:** each item's whole-item vector (a video's mean) of the library's search model, from the in-memory vector index. The loose tier also finds candidates by vector search (16 nearest).
- **Groups** are connected sets of matching items, so they don't depend on the order items are checked in, and the incremental and full paths give the same groups (E12-S02-T04).
- **Exact copies** count as one item, shown by the copy with the lowest ID; the others stay only in their exact group. So exact copies are never visual duplicates of each other.

### Sensitivity (E12-S02-T03)

Each library stores its sensitivity in `LibrarySettings.VisualDuplicateSensitivity` (settings JSON, written only when loose; strict is the default). Settings › Library › Maintenance shows Strict and Loose and **Find duplicates now**. A change checks every item again: it starts the full pass (`DetectDuplicatesCommand`); if one is already running, every item is queued and `DuplicateGroupMaintainer` works off what that pass leaves. Groups both sensitivities find keep their ID and review state (D578).

Synthetic animations of one generator look alike to the model (the `gif-animated` and `webp-animated` samples have a cosine of 0.96 to 0.98), so loose groups them, as it groups other different items that look this alike.

### Measurements (2026-11-27)

SigLIP 2 base on the evaluation corpus (212 photos, 60 documents and screenshots), with copies made in memory. Recall is the share of copies matched with their original; false positives are pairs of different originals.

| Copy | Photos strict | Photos loose | Docs strict | Docs loose |
|---|---|---|---|---|
| JPEG quality 30 | 100 % | 100 % | 93 % | 100 % |
| Half size | 99 % | 100 % | 92 % | 100 % |
| 320-pixel thumbnail | 100 % | 100 % | 90 % | 98 % |
| Brightness and contrast edit | 97 % | 100 % | 82 % | 100 % |
| 4 % crop | 4 % | 100 % | 0 % | 97 % |
| 8 % crop | 0 % | 96 % | 0 % | 55 % |
| **False positives** (22,366 + 1,770 pairs) | 0 | 0 | 0 | 0 |

- Unrelated photos are never closer than 16 bits, and documents 12. Different photos of one subject (four of St Basil's Cathedral) reach a cosine of 0.938, which is why the embedding tier starts at 0.95.
- Strict also groups light tonal edits: neither the hash nor the embedding tells them from the original.
- The test set in `tests/SampleMedia/near-duplicates` holds features of some of these photos (see its README).
