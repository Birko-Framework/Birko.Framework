---
id: TASK-790
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
priority: P2
assignee: ai
created: 2026-10-30
depends-on: [TASK-789]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Parity list: Birko.Web.Shell, one row per file

## Context

FEATURE-021 D4, D5. Birko.Web.Shell has no `b-*` components; it is modules (auth, commands, connection, dashboard,
feedback, modules, notifications, pages, shell, tenants) of `.ts` files. Add a second section to the parity list with
one row per `Birko.Web.Shell/src/**/*.ts` except `index.ts` (about 35 rows), mapped to Birko.Xaml.Shell / Birko.Xaml.Core
(e.g. `base-list-page.ts` → `ListPageViewModel` + `ListPageView`, `b-app-shell.ts` → `RibbonShellView`). Type-only
files (`*-types.ts`) are usually `n/a`. Extend TASK-789's test to the Shell section with the same rules (no row, stale
row, skip reason, unknown status, duplicate).

## Acceptance criteria

- [x] One row per non-index `.ts` in Birko.Web.Shell, each with a status and, unless `n/a` / `missing`, its desktop counterpart.
- [x] The test applies the TASK-789 rules to the Shell section; a fixture test per rule.
- [x] Proved it can fail: a temporary `src/pages/base-foo-page.ts` fails the test, naming it.

## Out of scope

- Building any missing shell piece.

## Human test plan

- [ ] 1. Read the Shell section: each mapping matches the desktop shell (list/detail/split pages, ribbon/mobile/sidebar shells).
- [x] 2. Add an empty `Birko.Web.Shell/src/pages/base-foo-page.ts` in the Birko\Web checkout: the test fails naming it; remove it: it passes.
  - **Passed 2026-10-30:** run by the agent (an automated check): the Shell case failed with `pages/base-foo-page exists on the web but has no row`; removed, it passed.

## Implementation plan

1. `PARITY.md` `## Shell`: one row per `Birko.Web.Shell/src/**/*.ts` except `index.ts`, keyed by its path under `src`
   without `.ts` (`pages/base-list-page`) — file names alone read worse and a module folder is context.
2. Statuses from reading each file against Birko.Xaml.Core / .Shell / .Avalonia. No new `skipped` rows: FEATURE-021
   decided nine skips, so a web-only file (e.g. the URL-fragment auth session) is `missing` and named in the review
   log as a skip candidate for the maintainer, not skipped unasked.
3. `WebCheckout.ShellNames`; the live test runs per section (a `[Theory]` over Components / Shell). `Validate` is
   section-agnostic, so its fixture tests (TASK-789) already pin every rule; add a fixture for path-shaped names.

## Review log

- 2026-10-30 — Built; **verify** (step 1 needs the maintainer's judgement on the mappings). `PARITY.md` `## Shell`:
  35 rows — 17 built, 13 missing, 5 n/a, no skips. Keyed by path under `src`. `WebCheckout.ShellNames` / `Names`;
  the live test is a `[Theory]` over both sections; one fixture for path-shaped names (`Validate` is
  section-agnostic, so TASK-789's fixtures pin every rule for both).
- **Skip candidate for the maintainer** (left `missing`, because FEATURE-021 decided the skips and it was not among
  them): `auth/auth-session`, which reads tokens from the browser redirect's URL fragment. Deciding whether desktop sign-in, permissions, notifications or a dashboard are wanted is a
  separate feature, not this list's job (D12).
- Proved it can fail: a temporary `src/pages/base-foo-page.ts` failed the Shell case naming it; removed, green.
  Avalonia parity tests 11/11.
- Gate (inline passes): conventions, intent (all three criteria), correctness — no blocker; security n/a (test code,
  Markdown); comments — the two new doc comments state what the methods return.
