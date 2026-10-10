---
id: TASK-551
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# I18n.t() fills placeholders one after another, so a value holding "{other}" is rewritten by the next one

## Context

Reported by FlowerFurStudio (its TASK-106, found by `/code-review` 2026-10-07 in the server-side `StorefrontText`,
whose acceptance criteria ask the client side to be checked). **Confirmed in Birko.Web.Core** (2026-10-10):
`Birko.Web.Core/src/i18n/i18n.ts:103-106` loops over `params` and runs one `value.replace(...)` per key, so a value
inserted for `{label}` is scanned again for `{value}`: `t('x', { label: 'Barva {value}', value: 'hnědá' })` on
`"{label}: {value}"` renders `Barva hnědá: hnědá`. Params are often shop data (names, labels), so the text shown can
differ from both.

Two more defects on the same lines:

- **`$` patterns in a value are expanded.** `String.prototype.replace` with a string replacement interprets `$&`,
  `` $` ``, `$'`, `$1`; a value `"$&"` inserts the matched `{name}` instead.
- **The key is not regex-escaped.** `new RegExp(`\\{${k}\\}`)` breaks for a param key containing a regex
  metacharacter.

Sequenced after the storefront components TASK-548–550 (user decision 2026-10-10).

## Acceptance criteria

- [ ] Interpolation is **one pass over the template** (a single regex over `\{(\w+)\}` with a replacer function);
      an inserted value is never scanned again
- [ ] A value containing `$&` / `$1` is inserted literally
- [ ] An unknown `{name}` with no param is left as is (current behaviour, now pinned by a test)
- [ ] Regression tests for all three; the first is proved to fail on the old code
- [ ] Plural forms path (if it interpolates separately) uses the same function — one producer
- [ ] Reply on FFS TASK-106 that the client side is fixed, so both sides agree

## Out of scope

- The server-side `StorefrontText` fix — FFS TASK-106 owns it

## Human test plan

N/A — pure string logic, fully covered by the regression tests.

## Implementation plan

_Populated by `/tasks plan TASK-551` — leave empty until then._
