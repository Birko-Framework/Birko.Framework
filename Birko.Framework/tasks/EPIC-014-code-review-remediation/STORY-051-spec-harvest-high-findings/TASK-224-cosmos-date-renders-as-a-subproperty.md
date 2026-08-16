---
id: TASK-224
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-223, TASK-222]
pr: null
github-issue: null
jira-key: null
findings: []
---

# `DateTime.Date` in a CosmosDB filter renders as a JSON sub-property and silently matches nothing

## Context

Found by [[TASK-223]], whose connection-mode fix let `CosmosFilterMatrixLiveTests` run for the first
time. First run: **26 of 27 OK**, and the 27th is silent.

```
dateDotDate  -> DIVERGE oracle=1 actual=0        x => x.CreatedAt.Date == d2
```

Rendered offline against the SDK (no emulator needed — `ToQueryDefinition()` renders locally):

| written as | emitted SQL |
|---|---|
| `x => x.CreatedAt == d2` | `WHERE (root["CreatedAt"] = "2026-01-03T00:00:00Z")` |
| **`x => x.CreatedAt.Date == d2`** | **`WHERE (root["CreatedAt"]["Date"] = "2026-01-03T00:00:00Z")`** |
| `x => x.CreatedAt >= d2 && x.CreatedAt < d2.AddDays(1)` | `WHERE ((root["CreatedAt"] >= "…03…") AND (root["CreatedAt"] < "…04…"))` |

**The provider treats `.Date` as a JSON sub-property access.** Cosmos stores a `DateTime` as an ISO
string, so `root["CreatedAt"]["Date"]` addresses a member of a string — which does not exist. The
comparison is never true, the query is valid, and **zero rows come back with no error**.

This is the worst class in this family, the same one as RavenDB's dropped ternary ([[TASK-222]]): not a
refusal a caller can react to, but a plausible wrong answer. `.Date` is an entirely ordinary thing to
write when filtering "on this day".

**The correct translation already exists in this framework.** The SQL connector rewrites `.Date` to a
half-open range — CLAUDE.md § Conventions records it as `(T.Seen >= @a AND T.Seen < @b)` — and the third
row above shows Cosmos renders exactly that shape correctly. MongoDB's matrix reports `dateDotDate` OK,
so this is Cosmos-specific.

## Approach

1. Rewrite `x.Member.Date == constant` to `x.Member >= constant.Date && x.Member < constant.Date.AddDays(1)`
   in the CosmosDB filter path, beside `SpanContains.Rewrite` (TASK-220) — same entry points, same shape
   of fix.
2. **Decide the scope deliberately.** `.Date` also appears in `>`, `<`, `>=`, `<=` and `!=` comparisons,
   and on both sides. Equality is the shape the matrix covers and the one most likely written; the others
   either need the same treatment or an explicit refusal. Do not silently handle only `==`.
3. Check whether the SQL connector's existing `.Date` rewrite can be shared rather than re-derived — the
   one-producer rule. If its shape is SQL-specific, say so rather than duplicating quietly.
4. ⚠ **A `.Date` on the *right*-hand side, or `.Date` compared to `.Date`, must not be mistranslated into
   something that looks right.** A wrong range is the same class of defect as the current wrong property.

## Acceptance criteria

- [ ] `x => x.When.Date == d` returns the same rows on CosmosDB as the compiled-delegate oracle
- [ ] The other comparison operators against `.Date` are each handled or explicitly refused, with the
      decision recorded — none left silently emitting `["Date"]`
- [ ] `CosmosFilterMatrixLiveTests` reports 27 of 27 against the emulator
- [ ] A **non-gated** test pins the emitted SQL — `ToQueryDefinition()` renders locally, so the wrong
      property name is catchable without a server
- [ ] Red-verified with the split as numbers; contract pins named as pins

## Out of scope

- [[TASK-223]]'s connection mode, which is what made this visible.
- The equivalent audit on RavenDB — its matrix reports `dateDotDate` OK.

## Human test plan

- [ ] Against the emulator (`--protocol http`, `BIRKO_COSMOS_CONNECTION_MODE=Gateway`), filter a
      collection by "created on this day" and confirm the rows come back. Worth one manual look because
      the failure mode is an empty result, indistinguishable from "nothing matched" in any log.

## Implementation plan

_Populated by `/tasks plan TASK-224` — leave empty until then._
