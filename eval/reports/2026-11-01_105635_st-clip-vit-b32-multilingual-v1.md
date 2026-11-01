# Evaluation report: st-clip-vit-b32-multilingual-v1

- Run: 2026-11-01 10:56:35 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.8349 | - | - | no baseline |
| mrr | en | 0.8419 | - | - | no baseline |
| recall@10 | ru | 0.5564 | - | - | no baseline |
| mrr | ru | 0.5175 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.5938 | 0.7538 |
| abstract | ru | 8 | 0.4938 | 0.5667 |
| distractor | en | 11 | 0.9909 | 0.8258 |
| distractor | ru | 11 | 0.5182 | 0.3803 |
| media | en | 8 | 0.8021 | 0.8170 |
| media | ru | 8 | 0.5417 | 0.4854 |
| object | en | 31 | 0.8967 | 0.9358 |
| object | ru | 31 | 0.5983 | 0.6488 |
| scene | en | 22 | 0.8076 | 0.7982 |
| scene | ru | 22 | 0.6258 | 0.5952 |
| synonym | en | 7 | 0.7907 | 0.8929 |
| synonym | ru | 5 | 0.2822 | 0.4654 |
| text | en | 18 | 0.8056 | 0.7736 |
| text | ru | 25 | 0.5400 | 0.3517 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| en-095 | text | 1 | - | 0.0000 |
| ru-011 | object | 3 | - | 0.0000 |
| ru-012 | object | 3 | - | 0.0000 |
| ru-015 | object | 4 | - | 0.0000 |
| ru-026 | object | 3 | - | 0.0000 |
| ru-053 | scene | 2 | - | 0.0000 |
| ru-063 | distractor | 3 | - | 0.0000 |
| ru-064 | distractor | 1 | - | 0.0000 |
| ru-067 | distractor | 1 | - | 0.0000 |
| ru-077 | synonym | 3 | - | 0.0000 |
| ru-084 | text | 2 | - | 0.0000 |
| ru-093 | text | 1 | - | 0.0000 |
| ru-004 | object | 3 | 96 | 0.0000 |
| en-015 | object | 4 | 90 | 0.0000 |
| ru-006 | object | 3 | 72 | 0.0000 |

