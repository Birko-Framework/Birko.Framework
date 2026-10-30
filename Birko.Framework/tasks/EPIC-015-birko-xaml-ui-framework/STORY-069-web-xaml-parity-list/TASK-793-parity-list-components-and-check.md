---
id: TASK-793
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-10-30
depends-on: []
blocks: [TASK-794, TASK-795, TASK-796]
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

- [x] The list has one row per `b-*.ts` in Birko.Web.Components, seeded from the 2026-10-30 survey, the D11 nine `skipped` with reasons.
- [x] The test fails on: a component with no row, a row with no component, a `skipped` row with no reason, an unknown status, a duplicate row — each tested against a fixture list.
- [x] Without the web checkout the test skips with a stated reason; with `BIRKO_REQUIRE_LIVE` it fails.
- [x] Proved it can fail against the real checkout: a temporary `b-foo.ts` fails the test, naming `b-foo`.
- [x] Birko.Xaml.Avalonia CLAUDE.md and README point at the list; Recent Updates entry.

## Out of scope

- Shell rows (TASK-794), the named-counterpart check (TASK-795), CI (TASK-796).
- Porting any `missing` component (D12).

## Human test plan

- [ ] 1. Read the list: every status matches what you know of the desktop library (no `built` for something that is only a form field, no `missing` for something you have used).
- [ ] 2. The nine skipped components each carry a reason you agree with.
- [x] 3. In the Birko\Web checkout, add an empty `b-foo.ts` under `Birko.Web.Components/src/data`; run `dotnet test --nologo` in `tests/Birko.Xaml.Avalonia.Tests`: it fails and names `b-foo`. Remove the file: it passes.
  - **Passed 2026-10-30:** run by the agent, an automated check rather than a judgement: with `b-foo.ts` the live test failed with `b-foo exists on the web but has no row`; removed, it passed.

## Implementation plan

1. `Birko.Xaml.Avalonia/PARITY.md` — header (what the list is, the seven statuses, how the check works, how to run it),
   then `## Components`: one table, `| Web component | Status | Desktop | Note |`, one row per `b-*` (name only, they are
   unique across folders), grouped by the web folder. Seed from the 2026-10-30 survey; the D11 nine `skipped`.
   The Desktop column is free text here; TASK-795 gives it a resolvable notation.
2. `tests/Birko.Xaml.Avalonia.Tests/Parity/ParityList.cs` — pure: parse the `## Components` table out of the Markdown
   (rows between that heading and the next `## `), and `Validate(rows, webNames)` returning problem strings: no row,
   stale row, unknown status, duplicate, `skipped` without a note. No I/O, so fixture tests feed it strings.
3. `Parity/WebCheckout.cs` — locate `Birko\Web` as `DiffVectors` does (`BIRKO_SRC`'s sibling, else walk up to the dir
   holding `Framework`); `PARITY.md` by walking up from the test binary to `Birko.Xaml.Avalonia/PARITY.md`. Not shared
   with `DiffVectors`: the two test projects have no common helper project, and linking a file across them is more
   machinery than the eight lines it would save.
4. `ParityListTests.cs` — fixture tests per rule; the live test (skip with a reason / fail under `BIRKO_REQUIRE_LIVE`)
   asserts no problems against the real checkout.
5. Prove it can fail: temporary `b-foo.ts` in the web checkout.
6. Docs: Avalonia CLAUDE.md + README pointer, tests CLAUDE.md, framework Recent Updates.

## Review log

- 2026-10-30 — Built; **verify** (Human test plan steps 1–2 need the maintainer's judgement on the statuses and skip
  reasons). `Birko.Xaml.Avalonia/PARITY.md`: 70 rows (20 built, 22 themed, 6 form-only, 5 needs-theme, 8 missing, 9
  skipped). Tests: `Parity/ParityList.cs` (parse a section, `Validate` returns every problem),
  `Parity/WebCheckout.cs` (locator; not shared with `DiffVectors`, as the plan says), `ParityListTests` 9.
- **Proved it can fail**, on the real checkout: a temporary `b-foo.ts` failed the live test naming `b-foo`; renaming the
  `b-stat` row to `b-statx` failed it with `line 48: b-statx has a row but no longer exists on the web`; both reverted,
  green. The skip path, by running the built test DLL from a folder outside the Birko tree: passes with
  `SKIPPED: no Birko\Web checkout …`; with `BIRKO_REQUIRE_LIVE=1` fails with the `InvalidOperationException`.
- Avalonia suite 412/412. No new warnings in the new files.
- Gate (inline passes): conventions — xUnit + FluentAssertions, guard clauses, nullable clean, docs + Recent Updates
  updated, no new cross-cutting pattern beyond the locator already used by `DiffVectors`; intent — all five criteria
  built, Out of scope respected (no shell rows, no counterpart check, no CI, no ports); correctness — no blocker
  found; security — not applicable (test-only code and a Markdown file); comments — the new doc comments describe
  what the code does and point at `DiffVectors` rather than restating it. `ci: main RED — diff-parity failure at
  efb186e7` (unrelated to this diff; relevant to TASK-796, which models its job on that workflow).
