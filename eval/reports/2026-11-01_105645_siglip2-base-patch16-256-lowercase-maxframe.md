# Evaluation report: siglip2-base-patch16-256-lowercase-maxframe

- Run: 2026-11-01 10:56:45 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9601 | - | - | no baseline |
| mrr | en | 0.9794 | - | - | no baseline |
| recall@10 | ru | 0.8069 | - | - | no baseline |
| mrr | ru | 0.8438 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.7625 | 0.8542 |
| abstract | ru | 8 | 0.3562 | 0.5449 |
| distractor | en | 11 | 0.9818 | 1.0000 |
| distractor | ru | 11 | 0.9909 | 1.0000 |
| media | en | 8 | 0.9167 | 1.0000 |
| media | ru | 8 | 0.6667 | 0.7995 |
| object | en | 31 | 0.9836 | 1.0000 |
| object | ru | 31 | 0.8973 | 0.9543 |
| scene | en | 22 | 0.9795 | 0.9773 |
| scene | ru | 22 | 0.8091 | 0.8772 |
| synonym | en | 7 | 0.9333 | 1.0000 |
| synonym | ru | 5 | 0.7289 | 0.9000 |
| text | en | 18 | 1.0000 | 0.9722 |
| text | ru | 25 | 0.8167 | 0.7071 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-057 | abstract | 1 | 93 | 0.0000 |
| ru-055 | abstract | 4 | 66 | 0.0000 |
| ru-040 | scene | 4 | 21 | 0.0000 |
| ru-109 | media | 2 | 16 | 0.0000 |
| en-055 | abstract | 4 | 3 | 0.2500 |
| ru-059 | abstract | 4 | 1 | 0.2500 |
| ru-082 | text | 4 | 1 | 0.2500 |
| ru-014 | object | 6 | 3 | 0.3333 |
| ru-039 | scene | 3 | 1 | 0.3333 |
| ru-110 | media | 6 | 1 | 0.3333 |
| ru-032 | scene | 16 | 4 | 0.4000 |
| ru-058 | abstract | 5 | 3 | 0.4000 |
| ru-076 | synonym | 5 | 2 | 0.4000 |
| ru-016 | object | 5 | 1 | 0.4000 |

