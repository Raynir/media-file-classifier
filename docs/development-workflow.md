# Development workflow

The branching and commit rules (D064).

## Branches

| Branch | Purpose |
|--------|---------|
| `main` | The latest finished milestone. Changes only through release and hotfix merges. |
| `develop` | Integration branch. Feature branches start here and merge back here. |
| `feature/MFC-<key>-<short-description>` | One sub-task, e.g. `feature/MFC-222-solution-layout`. A Story whose sub-tasks are tightly coupled can use one branch for the whole Story, e.g. `feature/MFC-51-solution-structure`. |
| `release/<version>` | Staging for a release, where the final tests run. |
| `hotfix/<version>` | An urgent fix to `main`, e.g. `hotfix/1.0.1`. |

## Releases

- **MVP milestones:** at the end of each milestone, cut `release/M0` … `release/M5` from `develop`.
  1. Test the branch, and update `CHANGELOG.md`.
  2. Merge it into `main` and back into `develop`.
  3. Tag `main` as `m0` … `m4`. `release/M5` is tagged `v1.0.0` (D058).
- **After the MVP:** use `release/1.1.0`, `release/1.2.0` and so on, with a `v1.x.0` tag.
- **Hotfixes:** a hotfix branches from `main`, and merges into `main` and `develop`, with a `v1.x.y` tag.

## Commits

- Keep each commit atomic: one logical change that builds on its own.
- End the message with the Jira key of the sub-task it implements:

  ```text
  Add shared build configuration MFC-224
  ```

- The key links the commit to the issue's Development panel in Jira.

## Pull requests

- Open pull requests from `feature/*` into `develop`.
- The title names the Story or sub-task key.
- The build and tests must pass (CI arrives with E01-S11).
