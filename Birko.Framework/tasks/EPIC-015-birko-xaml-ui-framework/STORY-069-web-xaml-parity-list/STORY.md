---
id: STORY-069
parent: EPIC-015
status: todo
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

1. [TASK-789](TASK-789-parity-list-components-and-check.md) — the list for `b-*` components and the check: no row, stale row, skip reason, skip without the web checkout (D1–D3, D6, D7, D9–D12)
2. [TASK-790](TASK-790-parity-list-shell-files.md) — Birko.Web.Shell rows, one per file (D4, D5)
3. [TASK-791](TASK-791-parity-check-named-controls-exist.md) — the check that a named desktop counterpart exists (D8)
4. [TASK-792](TASK-792-parity-ci-job.md) — a CI job with the web checkout and `BIRKO_REQUIRE_LIVE` (D13)

TASK-790, TASK-791 and TASK-792 each depend only on TASK-789 and can run in any order.

## Success criteria

- Every `b-*.ts` in Birko.Web.Components and every non-index `.ts` in Birko.Web.Shell has exactly one row.
- Adding a `b-*.ts` to the web checkout fails the test, naming it; so does deleting one that has a row.
- CI runs the test with the web checkout present, so a skip cannot pass as green.
