# Evaluation report: metaclip2-worldwide-s16

- Run: 2026-11-01 10:56:38 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.8935 | - | - | no baseline |
| mrr | en | 0.9585 | - | - | no baseline |
| recall@10 | ru | 0.8390 | - | - | no baseline |
| mrr | ru | 0.8345 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.6188 | 0.9167 |
| abstract | ru | 8 | 0.3812 | 0.4398 |
| distractor | en | 11 | 0.9818 | 0.8409 |
| distractor | ru | 11 | 0.9909 | 0.9545 |
| media | en | 8 | 0.8125 | 1.0000 |
| media | ru | 8 | 0.7812 | 0.8929 |
| object | en | 31 | 0.9433 | 1.0000 |
| object | ru | 31 | 0.8773 | 0.9328 |
| scene | en | 22 | 0.8879 | 0.9773 |
| scene | ru | 22 | 0.8856 | 0.9470 |
| synonym | en | 7 | 0.8984 | 1.0000 |
| synonym | ru | 5 | 0.8489 | 0.7400 |
| text | en | 18 | 0.9167 | 0.9199 |
| text | ru | 25 | 0.8467 | 0.6874 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| en-095 | text | 1 | 17 | 0.0000 |
| ru-061 | abstract | 1 | 17 | 0.0000 |
| ru-056 | abstract | 1 | 15 | 0.0000 |
| en-058 | abstract | 5 | 3 | 0.2000 |
| ru-055 | abstract | 4 | 7 | 0.2500 |
| en-055 | abstract | 4 | 1 | 0.2500 |
| ru-030 | object | 3 | 4 | 0.3333 |
| en-046 | scene | 3 | 1 | 0.3333 |
| ru-031 | object | 3 | 1 | 0.3333 |
| ru-046 | scene | 3 | 1 | 0.3333 |
| ru-054 | abstract | 10 | 1 | 0.4000 |
| ru-058 | abstract | 5 | 1 | 0.4000 |
| ru-081 | text | 2 | 8 | 0.5000 |
| ru-109 | media | 2 | 7 | 0.5000 |
| ru-101 | text | 2 | 4 | 0.5000 |

