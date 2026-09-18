---
id: TASK-457
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-036, TASK-131, TASK-226, TASK-228, TASK-449]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Consolidate the 349-repo framework polyrepo into one repo under a GitHub org

## Context

The framework was **365 git repos** — 178 `Framework/Birko.*`, 167 `Framework.Tests/Birko.*.Tests`,
4 `Web/`, 16 `Consumers/` — all on the personal account `github.com/birko`. [[TASK-036]] created the
bucket layout in June; `docs/adr/ADR-001` (the former untracked `WORKSPACE-STRUCTURE.md`) left
"monorepo?" as its open question #1. This closes it.

**Result:** `Birko-Framework/Birko.Framework` (178 projects + 167 test projects under `tests/`),
`Birko-Framework/Birko.Web` (4 packages), `Birko-Framework/Birko.Sandbox`. Consumers untouched.

## What decided it

- **[[TASK-131]] was a defect caused by the polyrepo.** All 25 spec areas globbed out of the
  aggregator's repo while `generated-at` stamped only that repo's HEAD, so `/specs verify`'s
  staleness primitive could never observe a source change — *decorative*, not merely weak. One repo
  fixes it, and `docs/specs/.map.yml` now records that.
- **A fix and its test could not be atomic.** `git bisect` ran tests from the test repo's HEAD (a
  tree from another day); `git revert` of a fix left its test asserting the fixed behaviour. For a
  codebase whose method is mutation testing, code and tests had to be one versioned unit.
- **Size was measured away.** 178 repos = ~47 MB of history; the consolidated repo packs to **14 MB**.
  The 200 MB aggregator turned out to be 6,566 never-gc'd loose objects, so **no history rewrite was
  needed** — `mermaid.min.js` and the 1.5 MB audit file stayed.

## Verification

- **349/349 repos absorbed, 0 failures.** `git filter-repo --to-subdirectory-filter` + merge.
- **Commit reconciliation exact:** 4,096 = 2,676 + 1,074 + 1 root + 345 merges.
- **History intact:** oldest commit 2019-03-15 preserved; `git log --follow` and `git blame` resolve
  back through renames (`Birko.Data.SQL/Attribute/` → `Attributes/`).
- **1,516 import references rewritten** across 167 `.csproj` (`..\..\Framework\` → `..\..\`),
  **0 unresolved**; `.slnx` 344/345 and `.code-workspace` 345/349 in-repo, remainder cross-bucket
  (`Consumers/`, `Web/`) and correct.
- **Tests match the original tree exactly** (control-run): Core 102=102, InMemory 74=74; JSON 23,
  XML 18, Random 130 all green.

## Gotchas worth carrying

- **Windows MAX_PATH bit twice.** `--to-subdirectory-filter` nests the project name, so
  `Birko.Communication.OAuth.Providers.Tests` under a long scratch path exceeded 260 chars — git
  needed `core.longpaths=true`, and Python's `io.open` failed outright until the tree was moved to a
  short path. The real location (`C:\Source\Birko\Framework`) is fine at ~119 chars max.
- **`safe.directory` is protected-config only.** Some `.git` dirs were `BUILTIN\Administrators`-owned,
  so `git clone` refused them while `git -C` reads worked. `-c safe.directory=*` is ignored by design;
  a temp `GIT_CONFIG_GLOBAL` that `[include]`s the real one works and touches nothing permanent.
- **`TaskStop` killed a parent but not its `bash` child**, so two migration runs interleaved and
  collided on a shared remote name. Fixed with a PID lock and `git fetch <path>` + `FETCH_HEAD`
  instead of named remotes.
- **46 repos had unpushed commits**, so the local disk — not GitHub — was the source of truth. The
  migration cloned from local paths.
- **Actions only reads `.github/workflows/` from the repository root.** The four `token-parity.yml`
  copies existed per-repo *on purpose* (a gate on the source cannot catch an edit to the output);
  three became inert directories on migration. Replaced with one root workflow per repo, down from
  four checkouts to two.

## Follow-ups

- [[TASK-226]] — its premise partly dissolved; re-examine rather than leaving it `todo`.
- [[TASK-228]] — Birko.Sandbox now has a remote; can close.
- `PREZENTACIA.md` — see [[TASK-458]].
- **Not done:** 433 commits in WorkoutTracker/Presenter/BardStudio/Latent remain local-only, by
  explicit decision.
