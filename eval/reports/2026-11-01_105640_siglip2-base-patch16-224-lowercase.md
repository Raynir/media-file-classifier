# Evaluation report: siglip2-base-patch16-224-lowercase

- Run: 2026-11-01 10:56:40 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9659 | - | - | no baseline |
| mrr | en | 0.9786 | - | - | no baseline |
| recall@10 | ru | 0.8050 | - | - | no baseline |
| mrr | ru | 0.8344 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.8062 | 0.8438 |
| abstract | ru | 8 | 0.3062 | 0.5033 |
| distractor | en | 11 | 1.0000 | 1.0000 |
| distractor | ru | 11 | 1.0000 | 1.0000 |
| media | en | 8 | 0.9167 | 1.0000 |
| media | ru | 8 | 0.6979 | 0.6845 |
| object | en | 31 | 0.9868 | 1.0000 |
| object | ru | 31 | 0.9209 | 0.9339 |
| scene | en | 22 | 0.9689 | 0.9773 |
| scene | ru | 22 | 0.8053 | 0.8932 |
| synonym | en | 7 | 0.9619 | 1.0000 |
| synonym | ru | 5 | 0.7667 | 0.8000 |
| text | en | 18 | 1.0000 | 0.9722 |
| text | ru | 25 | 0.7767 | 0.7472 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-057 | abstract | 1 | 90 | 0.0000 |
| ru-055 | abstract | 4 | 66 | 0.0000 |
| ru-040 | scene | 4 | 25 | 0.0000 |
| ru-093 | text | 1 | 16 | 0.0000 |
| ru-058 | abstract | 5 | 2 | 0.2000 |
| ru-059 | abstract | 4 | 2 | 0.2500 |
| ru-082 | text | 4 | 1 | 0.2500 |
| ru-046 | scene | 3 | 9 | 0.3333 |
| ru-074 | synonym | 9 | 2 | 0.3333 |
| ru-039 | scene | 3 | 1 | 0.3333 |
| ru-110 | media | 6 | 1 | 0.3333 |
| ru-032 | scene | 16 | 2 | 0.4000 |
| ru-016 | object | 5 | 1 | 0.4000 |
| ru-109 | media | 2 | 7 | 0.5000 |

