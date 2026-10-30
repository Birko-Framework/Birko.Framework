---
id: STORY-069
parent: EPIC-015
status: done
created: 2026-10-30
feature: FEATURE-021
affects: [Birko.Xaml.Avalonia, Birko.Xaml.Shell, Birko.Web.Components, Birko.Web.Shell]
---

# Birko.Web ↔ Birko.Xaml parity list, kept honest by a test

## User story

As a Birko maintainer, I want one list saying where every Birko.Web component and shell file stands on the desktop —
built, a themed Avalonia control, form field only, needs a theme, missing, skipped with a reason, or n/a — and a test
that fails when the web side moves without the list, so a new web component cannot ship unnoticed by Birko.Xaml.

Decisions: [FEATURE-021](../../../docs/features/FEATURE-021-web-xaml-parity-list/decisions.md).

## Tasks (build order)

1. [TASK-793](TASK-793-parity-list-components-and-check.md) — the list for `b-*` components and the check: no row, stale row, skip reason, skip without the web checkout (D1–D3, D6, D7, D9–D12) — **done**
2. [TASK-794](TASK-794-parity-list-shell-files.md) — Birko.Web.Shell rows, one per file (D4, D5) — **done**
3. [TASK-795](TASK-795-parity-check-named-controls-exist.md) — the check that a named desktop counterpart exists (D8) — **done**
4. [TASK-796](TASK-796-parity-ci-job.md) — a CI job with the web checkout and `BIRKO_REQUIRE_LIVE` (D13) — **done**

TASK-794, TASK-795 and TASK-796 each depend only on TASK-793 and can run in any order.

> Renumbered 2026-10-30 from TASK-789–792: a parallel session filed EPIC-001 tasks under TASK-789–791. Commits
> before the renumber (b29629f0, 7864af8e, e5959c81, 741c0614) carry the old ids in their subjects.

## Success criteria

- Every `b-*.ts` in Birko.Web.Components and every non-index `.ts` in Birko.Web.Shell has exactly one row.
- Adding a `b-*.ts` to the web checkout fails the test, naming it; so does deleting one that has a row.
- CI runs the test with the web checkout present, so a skip cannot pass as green.
