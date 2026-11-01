# Evaluation report: siglip2-base-patch16-256-lowercase

- Run: 2026-11-01 11:00:15 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 79d74dd4af8b, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: 2026-11-01_105641_siglip2-base-patch16-256-lowercase.json

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9598 | 0.0000 | 0.9400 | pass |
| mrr | en | 0.9841 | 0.0000 | 0.9600 | pass |
| recall@10 | ru | 0.8148 | 0.0000 | 0.7900 | pass |
| mrr | ru | 0.8370 | 0.0000 | 0.8100 | pass |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.7625 | 0.8542 |
| abstract | ru | 8 | 0.3438 | 0.5456 |
| distractor | en | 11 | 1.0000 | 1.0000 |
| distractor | ru | 11 | 1.0000 | 1.0000 |
| media | en | 8 | 0.9167 | 1.0000 |
| media | ru | 8 | 0.6667 | 0.7578 |
| object | en | 31 | 0.9803 | 1.0000 |
| object | ru | 31 | 0.9109 | 0.9382 |
| scene | en | 22 | 0.9644 | 1.0000 |
| scene | ru | 22 | 0.8205 | 0.8773 |
| synonym | en | 7 | 0.9619 | 1.0000 |
| synonym | ru | 5 | 0.7689 | 0.9000 |
| text | en | 18 | 1.0000 | 0.9722 |
| text | ru | 25 | 0.8167 | 0.7105 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-061 | abstract | 1 | - | 0.0000 |
| ru-057 | abstract | 1 | 78 | 0.0000 |
| ru-055 | abstract | 4 | 54 | 0.0000 |
| ru-040 | scene | 4 | 20 | 0.0000 |
| ru-109 | media | 2 | 16 | 0.0000 |
| en-055 | abstract | 4 | 3 | 0.2500 |
| ru-059 | abstract | 4 | 1 | 0.2500 |
| ru-082 | text | 4 | 1 | 0.2500 |
| ru-014 | object | 6 | 3 | 0.3333 |
| ru-039 | scene | 3 | 1 | 0.3333 |
| ru-110 | media | 6 | 1 | 0.3333 |
| ru-032 | scene | 16 | 4 | 0.4000 |
| ru-058 | abstract | 5 | 3 | 0.4000 |
| ru-016 | object | 5 | 1 | 0.4000 |
| ru-074 | synonym | 9 | 1 | 0.4444 |

