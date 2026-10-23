---
id: TASK-765
parent: EPIC-001
status: done
priority: P2
assignee: ai
created: 2026-10-23
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-json-diff strings never translate: filed under bwc.daterange in en.json

## Context

Reported by the Symbio session (Symbio TASK-953), passed on by the user. `Birko.Web.Components/locales/en.json` had
b-json-diff's whole block (22 keys, with the `changeCount` plural) pasted inside `bwc.daterange`'s braces, so it
shipped as `bwc.daterange.jsonDiff.*`. `b-json-diff.ts` reads `bwc.jsonDiff.*`, so `label()` never found a key and
fell back to its English default in every locale. Nothing reported it. Symbio copied the structure into its en/sk/cs,
so its Slovak and Czech for these keys were dead too.

## Acceptance criteria

- [x] `jsonDiff` moved out of `bwc.daterange` to `bwc.jsonDiff`, keys and values unchanged (en.json is the only
      locale file the framework ships)
- [x] Audit: every en.json key no source reads, and every key `src/` reads that en.json lacks, listed
- [x] Guard naming the key, proved by mutation
- [x] b-json-diff smoke: an i18n override of `bwc.jsonDiff.title` renders in the title
- [x] CHANGELOG with the consumer migration

## Human test plan

N/A — fully covered by automated tests (the static key guard and json-diff-smoke). Symbio's sk/cs strings rendering in
b-json-diff after it moves its block is Symbio's check under TASK-953.

## Outcome (2026-10-23)

- **Moved.** `bwc.daterange.jsonDiff` → `bwc.jsonDiff`.
- **Audit, other findings.** Six keys that `src/` reads were missing from en.json and were added with the defaults
  `src/` already uses. `common.yes` and `common.no` (boolean cells in `cell-renderers.ts`) and `common.module` and
  `common.navigation` (Shell `module-nav-provider.ts`) are read by `t()` with no fallback, or with `t(k) || 'X'`,
  which never fires because `t()` returns the key. So without a consumer bundle they rendered the raw key. That was
  read from the code, not observed. `bwc.ribbon.scrollTabsLeft` / `scrollTabsRight` had `label()` defaults, so only
  their translations were missing. Fifteen en.json keys are read by no source:
  `bwc.common.confirm`, `bwc.common.search`, `bwc.ribbon.expand`, `bwc.ribbon.unpin` (Symbio still reads the two
  ribbon keys through `t()`), `bwc.daterange.presets`, `.nightsCount`, `.daysCount` and `bwc.daterange.preset.*` (8).
  They are kept, as a named list with reasons in the guard. Removing them is a key change for consumers and is
  not done here.
- **Guard.** New Playground `locale-keys-check.mjs`, static and with no build: 20/20. Under the mutation (jsonDiff
  back under daterange) it gave 18/64, exit 1, with 22 `missing: bwc.jsonDiff.* is read by …/b-json-diff.ts` and 22
  `unused: bwc.daterange.jsonDiff.*`. After the restore it is 20/20 again. It also fails on a stale entry in the kept
  list and on a `${…}` template prefix (`bwc.stat.`) that matches no key.
- **Smoke.** json-diff-smoke 76/76 with two new checks: the shipped `locales/en.json` has `bwc.jsonDiff.title`, and
  an override of it renders in the title. The second check is about the component, and it passed before the fix
  too. The file placement is proved by the first check and by the guard.
- **Gate.** `verify.mjs` reports 0 failing checks; entry-points 409/409. `tsc` was not run (not installed here).
