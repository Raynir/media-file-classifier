# Evaluation report: siglip2-base-patch16-384-lowercase-maxframe

- Run: 2026-11-01 10:56:46 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9630 | - | - | no baseline |
| mrr | en | 0.9730 | - | - | no baseline |
| recall@10 | ru | 0.8071 | - | - | no baseline |
| mrr | ru | 0.8206 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.7375 | 0.9167 |
| abstract | ru | 8 | 0.3438 | 0.5037 |
| distractor | en | 11 | 1.0000 | 1.0000 |
| distractor | ru | 11 | 1.0000 | 1.0000 |
| media | en | 8 | 0.9792 | 1.0000 |
| media | ru | 8 | 0.6667 | 0.7405 |
| object | en | 31 | 0.9792 | 1.0000 |
| object | ru | 31 | 0.9143 | 0.9086 |
| scene | en | 22 | 0.9682 | 0.9545 |
| scene | ru | 22 | 0.8311 | 0.8653 |
| synonym | en | 7 | 0.9603 | 1.0000 |
| synonym | ru | 5 | 0.8311 | 0.8500 |
| text | en | 18 | 1.0000 | 0.9352 |
| text | ru | 25 | 0.7567 | 0.7142 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-057 | abstract | 1 | 87 | 0.0000 |
| ru-055 | abstract | 4 | 56 | 0.0000 |
| ru-040 | scene | 4 | 27 | 0.0000 |
| ru-093 | text | 1 | 11 | 0.0000 |
| ru-096 | text | 1 | 11 | 0.0000 |
| ru-109 | media | 2 | 11 | 0.0000 |
| en-055 | abstract | 4 | 3 | 0.2500 |
| ru-059 | abstract | 4 | 2 | 0.2500 |
| ru-082 | text | 4 | 1 | 0.2500 |
| ru-110 | media | 6 | 1 | 0.3333 |
| ru-032 | scene | 16 | 4 | 0.4000 |
| ru-058 | abstract | 5 | 2 | 0.4000 |
| en-058 | abstract | 5 | 1 | 0.4000 |
| ru-100 | text | 2 | 4 | 0.5000 |

