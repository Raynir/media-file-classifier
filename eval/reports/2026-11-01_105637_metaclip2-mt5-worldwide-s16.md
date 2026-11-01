# Evaluation report: metaclip2-mt5-worldwide-s16

- Run: 2026-11-01 10:56:37 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.8998 | - | - | no baseline |
| mrr | en | 0.9420 | - | - | no baseline |
| recall@10 | ru | 0.8053 | - | - | no baseline |
| mrr | ru | 0.8372 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.6938 | 0.8042 |
| abstract | ru | 8 | 0.2688 | 0.5234 |
| distractor | en | 11 | 0.9909 | 0.8591 |
| distractor | ru | 11 | 0.9606 | 0.9545 |
| media | en | 8 | 0.8542 | 1.0000 |
| media | ru | 8 | 0.6875 | 0.6846 |
| object | en | 31 | 0.9359 | 1.0000 |
| object | ru | 31 | 0.8930 | 0.9401 |
| scene | en | 22 | 0.9144 | 0.9697 |
| scene | ru | 22 | 0.8402 | 0.9015 |
| synonym | en | 7 | 0.9018 | 1.0000 |
| synonym | ru | 5 | 0.8556 | 0.8667 |
| text | en | 18 | 0.8750 | 0.8720 |
| text | ru | 25 | 0.7967 | 0.7445 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-057 | abstract | 1 | 64 | 0.0000 |
| ru-056 | abstract | 1 | 35 | 0.0000 |
| en-095 | text | 1 | 34 | 0.0000 |
| ru-097 | text | 1 | 28 | 0.0000 |
| ru-108 | media | 2 | 23 | 0.0000 |
| en-058 | abstract | 5 | 1 | 0.2000 |
| ru-055 | abstract | 4 | 7 | 0.2500 |
| en-055 | abstract | 4 | 3 | 0.2500 |
| ru-040 | scene | 4 | 2 | 0.2500 |
| ru-037 | scene | 4 | 1 | 0.2500 |
| ru-082 | text | 4 | 1 | 0.2500 |
| ru-030 | object | 3 | 7 | 0.3333 |
| ru-016 | object | 5 | 2 | 0.4000 |
| ru-058 | abstract | 5 | 1 | 0.4000 |

