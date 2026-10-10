---
id: TASK-551
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
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

- [x] Interpolation is **one pass over the template** (a single regex over `\{(\w+)\}` with a replacer function);
      an inserted value is never scanned again
- [x] A value containing `$&` / `$1` is inserted literally
- [x] An unknown `{name}` with no param is left as is (current behaviour, now pinned by a test)
- [x] Regression tests for all three; the first is proved to fail on the old code
- [x] Plural forms path (if it interpolates separately) uses the same function — one producer
- [ ] Reply on FFS TASK-106 that the client side is fixed, so both sides agree — **not done here**: the
      FlowerFurStudio repo is mid-branch in another session (TASK-008/009); left for that session (see Resolution)

## Out of scope

- The server-side `StorefrontText` fix — FFS TASK-106 owns it

## Human test plan

N/A — pure string logic, fully covered by the regression tests.

## Implementation plan

Done directly.

## Resolution (2026-10-10)

New `Birko.Web.Core/src/i18n/interpolate.ts` — `interpolate(template, params)`, one regex scan with a replacer
function, exported from the package. It is now the **only** placeholder filler: `I18n.t()` (standard and plural
paths), the global `t()` English-fallback path and `BaseComponent.label()`'s per-instance attribute path all call
it (the latter two already scanned once, but each with its own copy — three producers became one). Behaviour:
one pass; values inserted literally (`$&`, `$1`, `$$`); keys `[\w.-]+` (dotted / dashed keys keep working, a key
with a regex metacharacter no longer throws — the old `new RegExp` did on `a(`); an unknown, `null` / `undefined`
or inherited (`{constructor}`) placeholder stays visible. Core CLAUDE.md (§ i18n rules + directory map) and README.

**Proof.** Playground `src/i18n-interpolate-smoke.ts` (private `I18n` instance, no global mutation): 14/14, all
suites green. Mutation — the old loop restored in `I18n.t()` — fails 5, reproducing FlowerFurStudio's exact text:
`"Barva hnědá: hnědá"`. Core and Components `tsc` clean.

**For FlowerFurStudio TASK-106:** its criterion "the client's `t()` checked" can be ticked as *fixed upstream in
Birko TASK-551* (`interpolate()` in Birko.Web.Core); its server-side `StorefrontText` still needs its own one-pass
fix. Not written into that repo from here because it is on a task branch in another session.
