---
id: TASK-790
parent: STORY-069
feature: FEATURE-021
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] One row per non-index `.ts` in Birko.Web.Shell, each with a status and, unless `n/a` / `missing`, its desktop counterpart.
- [ ] The test applies the TASK-789 rules to the Shell section; a fixture test per rule.
- [ ] Proved it can fail: a temporary `src/pages/base-foo-page.ts` fails the test, naming it.

## Out of scope

- Building any missing shell piece.

## Human test plan

- [ ] 1. Read the Shell section: each mapping matches the desktop shell (list/detail/split pages, ribbon/mobile/sidebar shells).
- [ ] 2. Add an empty `Birko.Web.Shell/src/pages/base-foo-page.ts` in the Birko\Web checkout: the test fails naming it; remove it: it passes.

## Implementation plan

## Review log
