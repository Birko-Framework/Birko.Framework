---
id: TASK-458
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-457]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Turn `PREZENTACIA.md` into published framework documentation

## Context

`PREZENTACIA.md` (56 KB, at the aggregator root) is a draft explaining **how Birko.Framework works**.
Nothing links to it. It is the only hand-written narrative overview in the tree: `docs/` holds 44
per-area references plus generated specs, with no "here is how the whole thing fits together" entry
point — which is exactly what a newcomer (or a consumer evaluating the framework) needs first.

Kept rather than deleted during [[TASK-457]] for that reason.

## Why it is NOT a wiki candidate by default

The standing argument against GitHub Wiki (recorded when the org was set up) is about `docs/specs/`:
they are generated from code, stamped with the generating commit, and reviewed as an
intended-vs-unintended behavioural diff — a wiki is a separate git repo with no branches or PRs, so a
regenerated spec could never appear in the PR diff.

**`PREZENTACIA.md` is the one doc that escapes all three objections**: it is hand-written, not
stamped, and not reviewed as a behavioural diff. So a wiki is defensible here where it is not
elsewhere. The recommendation is still `docs/` published via **GitHub Pages** — it then versions
alongside what it describes and can link into the generated specs — but that is this task's decision
to make, not a foregone one.

## Acceptance

1. Decide the destination: `docs/` + Pages (recommended), wiki, or Outline (where the Birko.Game
   engine design already lives).
2. Bring the content up to date — it predates the monorepo, so any repo-layout claims in it are stale.
3. Link it from `README.md` as the narrative entry point.
4. Either delete `PREZENTACIA.md` or make it the published source; do not leave two copies.
