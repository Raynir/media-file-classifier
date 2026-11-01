# Evaluation report: siglip2-base-patch16-256-onnx-int8-int8-lowercase

- Run: 2026-11-01 10:56:48 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.8395 | - | - | no baseline |
| mrr | en | 0.8735 | - | - | no baseline |
| recall@10 | ru | 0.4253 | - | - | no baseline |
| mrr | ru | 0.4204 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.5562 | 0.5382 |
| abstract | ru | 8 | 0.2250 | 0.1235 |
| distractor | en | 11 | 0.9424 | 0.7348 |
| distractor | ru | 11 | 0.3424 | 0.2819 |
| media | en | 8 | 0.6875 | 0.6844 |
| media | ru | 8 | 0.5000 | 0.3714 |
| object | en | 31 | 0.9130 | 0.9839 |
| object | ru | 31 | 0.4748 | 0.5729 |
| scene | en | 22 | 0.8462 | 0.9015 |
| scene | ru | 22 | 0.4939 | 0.5306 |
| synonym | en | 7 | 0.8086 | 1.0000 |
| synonym | ru | 5 | 0.4100 | 0.5074 |
| text | en | 18 | 0.8472 | 0.9181 |
| text | ru | 25 | 0.3833 | 0.2888 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-047 | scene | 3 | - | 0.0000 |
| ru-057 | abstract | 1 | - | 0.0000 |
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-064 | distractor | 1 | - | 0.0000 |
| ru-067 | distractor | 1 | - | 0.0000 |
| ru-068 | distractor | 1 | - | 0.0000 |
| ru-069 | distractor | 1 | - | 0.0000 |
| ru-072 | distractor | 1 | - | 0.0000 |
| ru-093 | text | 1 | - | 0.0000 |
| ru-090 | text | 1 | 97 | 0.0000 |
| ru-106 | media | 1 | 75 | 0.0000 |
| en-057 | abstract | 1 | 73 | 0.0000 |
| ru-083 | text | 1 | 72 | 0.0000 |
| ru-074 | synonym | 9 | 64 | 0.0000 |
| ru-006 | object | 3 | 58 | 0.0000 |

