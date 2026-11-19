# Keyword field weights on describe-2 (MFC-1000)

Corpus 351 items, queries 139 en / 142 ru, analysis `eval/analysis/qwen_qwen3-vl-4b-describe-2.json` (describe-2 prompt, D367/D380). `app-keyword` and `app-hybrid` (RRF k 10, keyword and vector weight 1, threshold 0.07,0.13, 200 candidates), each run with `evaluate --system app-* --analysis ... --field-weights <set>`. Fields not named in a set stay at 1. Each cell is recall@10 / MRR.

| Set | Field weights | Keyword en | Keyword ru | Hybrid en | Hybrid ru |
|---|---|--:|--:|--:|--:|
| flat (default) | all 1 | 0.6707 / 0.7578 | 0.5567 / 0.6124 | 0.9171 / 0.9574 | 0.8598 / 0.8866 |
| old | title 4, tags 3, filename 2 | 0.6736 / 0.7548 | 0.5567 / 0.6187 | 0.9171 / 0.9599 | 0.8598 / 0.8879 |
| title-heavy | title 4 | 0.6715 / 0.7509 | 0.5567 / 0.6187 | 0.9171 / 0.9574 | 0.8598 / 0.8866 |
| tags-heavy | tags 4 | 0.6733 / 0.7393 | 0.5575 / 0.6136 | 0.9183 / 0.9574 | 0.8598 / 0.8904 |
| description-light | description 0.5 | 0.6683 / 0.7584 | 0.5567 / 0.6124 | 0.9171 / 0.9612 | 0.8598 / 0.8866 |
| old + description-light | title 4, tags 3, filename 2, description 0.5 | 0.6713 / 0.7548 | 0.5567 / 0.6187 | 0.9171 / 0.9599 | 0.8598 / 0.8879 |
| title+filename | title 2, filename 2 | 0.6707 / 0.7668 | 0.5567 / 0.6174 | 0.9163 / 0.9612 | 0.8598 / 0.8828 |

## Reading

- No set restores English keyword-only search to its describe-1 level (0.707 / 0.834). The best moves are within 0.003 recall@10 and 0.009 MRR, and each one lowers some other number: old and tags-heavy lose English keyword MRR (tags-heavy falls below its baseline), title+filename loses 0.004 Russian hybrid MRR and 0.001 English hybrid recall.
- Hybrid recall@10 is the same (0.917 en, 0.860 ru) in every set but two that move it by 0.001.
- Conclusion: the drop comes from the describe-2 text itself, not from the weights. The flat default stays; no baseline changes.
