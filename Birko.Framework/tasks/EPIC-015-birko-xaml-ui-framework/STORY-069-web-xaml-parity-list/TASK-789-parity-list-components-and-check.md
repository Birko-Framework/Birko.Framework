---
id: TASK-789
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-30
depends-on: []
blocks: [TASK-790, TASK-791, TASK-792]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Parity list for the `b-*` components, and the test that keeps it complete

## Context

FEATURE-021 D1, D2, D3, D6, D7, D9, D10, D11, D12. Today nothing records which Birko.Web components Birko.Xaml has;
the 2026-10-30 survey (see the feature's idea.md) had to be done by hand.

- **The list** is a Markdown table in `Birko.Xaml.Avalonia` (e.g. `PARITY.md`), one row per
  `Birko.Web.Components/src/**/b-*.ts`: web component · status · desktop counterpart · note/reason. Statuses (D3):
  `built`, `themed` (an Avalonia control in the Birko look), `form-only`, `needs-theme`, `missing`, `skipped`, `n/a`.
  Seed it from the survey; mark the nine D11 components (tour, chat, gallery, segmented, button-group, toolbar, empty,
  skeleton, stale-banner) `skipped` with their reasons. The other gaps stay `missing` (D12: no ports here).
- **The test** (in `tests/Birko.Xaml.Avalonia.Tests`) parses that table and fails, naming each offender, when:
  a `b-*.ts` has no row (D6); a row names a component with no `b-*.ts` (D7); a `skipped` row has an empty reason (D9);
  a status is not one of the seven; a component is listed twice.
- **Locating the web checkout** (D10): as `tests/Birko.Xaml.Core.Tests/DiffVectors.cs` does — `BIRKO_SRC`'s sibling
  `Web`, else a walk up to the directory holding `Framework`; absent → skip with a line saying so, unless
  `BIRKO_REQUIRE_LIVE` is set, then fail. Share that locator rather than copying it, if it can be shared cleanly.

## Acceptance criteria

- [ ] The list has one row per `b-*.ts` in Birko.Web.Components, seeded from the 2026-10-30 survey, the D11 nine `skipped` with reasons.
- [ ] The test fails on: a component with no row, a row with no component, a `skipped` row with no reason, an unknown status, a duplicate row — each tested against a fixture list.
- [ ] Without the web checkout the test skips with a stated reason; with `BIRKO_REQUIRE_LIVE` it fails.
- [ ] Proved it can fail against the real checkout: a temporary `b-foo.ts` fails the test, naming `b-foo`.
- [ ] Birko.Xaml.Avalonia CLAUDE.md and README point at the list; Recent Updates entry.

## Out of scope

- Shell rows (TASK-790), the named-counterpart check (TASK-791), CI (TASK-792).
- Porting any `missing` component (D12).

## Human test plan

- [ ] 1. Read the list: every status matches what you know of the desktop library (no `built` for something that is only a form field, no `missing` for something you have used).
- [ ] 2. The nine skipped components each carry a reason you agree with.
- [ ] 3. In the Birko\Web checkout, add an empty `b-foo.ts` under `Birko.Web.Components/src/data`; run `dotnet test --nologo` in `tests/Birko.Xaml.Avalonia.Tests`: it fails and names `b-foo`. Remove the file: it passes.

## Implementation plan

## Review log
