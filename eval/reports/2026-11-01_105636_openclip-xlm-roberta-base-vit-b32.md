# Evaluation report: openclip-xlm-roberta-base-vit-b32

- Run: 2026-11-01 10:56:36 UTC
- Data: public, 351 items, queries 105 en, 110 ru, 80 OCR images
- Inputs: baselines.json 876bb2b2d5d9, manifest.json 87beade17c46, ocr-truth.json 40f9edc25f75, queries.json 01a008acea95
- Previous run: none, so there are no deltas.

## Summary

| Metric | Group | Value | Change | Baseline | Status |
|---|---|--:|--:|--:|---|
| recall@10 | en | 0.9031 | - | - | no baseline |
| mrr | en | 0.9241 | - | - | no baseline |
| recall@10 | ru | 0.8325 | - | - | no baseline |
| mrr | ru | 0.8802 | - | - | no baseline |

## Search by category

Recall@10 and MRR over the first 100 results.

| Category | Language | Queries | Recall@10 | MRR |
|---|---|--:|--:|--:|
| abstract | en | 8 | 0.6938 | 0.8542 |
| abstract | ru | 8 | 0.3875 | 0.6367 |
| distractor | en | 11 | 0.9818 | 0.8864 |
| distractor | ru | 11 | 0.9091 | 0.9091 |
| media | en | 8 | 0.8333 | 0.7542 |
| media | ru | 8 | 0.7500 | 0.8812 |
| object | en | 31 | 0.9703 | 0.9624 |
| object | ru | 31 | 0.9087 | 0.9839 |
| scene | en | 22 | 0.9038 | 1.0000 |
| scene | ru | 22 | 0.8826 | 0.9333 |
| synonym | en | 7 | 0.9063 | 1.0000 |
| synonym | ru | 5 | 0.7778 | 0.8105 |
| text | en | 18 | 0.8611 | 0.8658 |
| text | ru | 25 | 0.8400 | 0.7838 |

## Weakest queries

| Query | Category | Relevant | First relevant at | Recall@10 |
|---|---|--:|--:|--:|
| ru-064 | distractor | 1 | - | 0.0000 |
| ru-061 | abstract | 1 | 59 | 0.0000 |
| ru-055 | abstract | 4 | 30 | 0.0000 |
| en-095 | text | 1 | 24 | 0.0000 |
| en-088 | text | 1 | 23 | 0.0000 |
| ru-057 | abstract | 1 | 23 | 0.0000 |
| ru-108 | media | 2 | 20 | 0.0000 |
| ru-077 | synonym | 3 | 19 | 0.0000 |
| ru-093 | text | 1 | 17 | 0.0000 |
| ru-096 | text | 1 | 13 | 0.0000 |
| en-058 | abstract | 5 | 2 | 0.2000 |
| ru-040 | scene | 4 | 5 | 0.2500 |
| en-055 | abstract | 4 | 1 | 0.2500 |
| en-046 | scene | 3 | 1 | 0.3333 |
| ru-058 | abstract | 5 | 1 | 0.4000 |

