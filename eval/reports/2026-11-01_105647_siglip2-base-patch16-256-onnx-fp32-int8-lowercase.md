# Evaluation report: siglip2-base-patch16-256-onnx-fp32-int8-lowercase

- Run: 2026-11-01 10:56:47 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9581 | - | - | no baseline |
| mrr | en | 0.9635 | - | - | no baseline |
| recall@10 | ru | 0.5461 | - | - | no baseline |
| mrr | ru | 0.5672 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.7875 | 0.8125 |
| abstract | ru | 8 | 0.2250 | 0.3117 |
| distractor | en | 11 | 0.9909 | 0.9394 |
| distractor | ru | 11 | 0.4455 | 0.4628 |
| media | en | 8 | 0.9792 | 0.9167 |
| media | ru | 8 | 0.6250 | 0.5929 |
| object | en | 31 | 0.9828 | 0.9839 |
| object | ru | 31 | 0.4926 | 0.5677 |
| scene | en | 22 | 0.9576 | 1.0000 |
| scene | ru | 22 | 0.5841 | 0.5520 |
| synonym | en | 7 | 0.9333 | 1.0000 |
| synonym | ru | 5 | 0.3667 | 0.3740 |
| text | en | 18 | 0.9722 | 0.9722 |
| text | ru | 25 | 0.7367 | 0.7379 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-057 | abstract | 1 | - | 0.0000 |
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-067 | distractor | 1 | - | 0.0000 |
| ru-068 | distractor | 1 | - | 0.0000 |
| ru-069 | distractor | 1 | - | 0.0000 |
| ru-006 | object | 3 | 95 | 0.0000 |
| ru-034 | scene | 5 | 70 | 0.0000 |
| ru-072 | distractor | 1 | 68 | 0.0000 |
| ru-064 | distractor | 1 | 57 | 0.0000 |
| ru-047 | scene | 3 | 54 | 0.0000 |
| ru-055 | abstract | 4 | 54 | 0.0000 |
| ru-007 | object | 3 | 39 | 0.0000 |
| ru-039 | scene | 3 | 39 | 0.0000 |
| ru-029 | object | 3 | 38 | 0.0000 |
| ru-074 | synonym | 9 | 37 | 0.0000 |

