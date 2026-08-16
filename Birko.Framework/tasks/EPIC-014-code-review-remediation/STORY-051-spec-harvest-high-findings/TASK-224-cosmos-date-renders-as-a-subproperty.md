---
id: TASK-224
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-223, TASK-222]
pr: [Birko.Data.Core@0308617, Birko.Data.CosmosDB@9e94ace, Birko.Data.Core.Tests@cda65c0]
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

- [x] `x => x.When.Date == d` returns the same rows on CosmosDB as the compiled-delegate oracle —
      `dateDotDate` now OK against the emulator
- [x] The other comparison operators against `.Date` are each handled or explicitly refused, with the
      decision recorded — none left silently emitting `["Date"]`. **All six handled**, plus operand
      mirroring; member-vs-member explicitly left alone (it has no day bucket to become)
- [x] `CosmosFilterMatrixLiveTests` reports **27 of 27** against the emulator
- [x] A **non-gated** test pins the emitted SQL — `ToQueryDefinition()` renders locally, so the wrong
      property name is catchable without a server. Met by `The_Date_member_is_gone_from_the_tree`, which
      asserts on the **expression tree** rather than the SQL: the rewrite is backend-agnostic, and a tree
      with no `.Date` in it cannot produce `["Date"]` in any provider
- [x] Red-verified with the split as numbers; contract pins named as pins — see step 6

## Out of scope

- [[TASK-223]]'s connection mode, which is what made this visible.
- The equivalent audit on RavenDB — its matrix reports `dateDotDate` OK.

## Human test plan

- [x] Against the emulator (`--protocol http`, `BIRKO_COSMOS_CONNECTION_MODE=Gateway`), filter a
      collection by "created on this day" and confirm the rows come back. Worth one manual look because
      the failure mode is an empty result, indistinguishable from "nothing matched" in any log.
      **Done** — the matrix suite is exactly that filter against a live emulator, and it went from
      `DIVERGE oracle=1 actual=0` to `OK`.

## Outcome

**What was broken.** `x.When.Date == d` on CosmosDB returned **zero rows with no error**. The provider
translates `.Date` as a JSON sub-property access — `WHERE (root["CreatedAt"]["Date"] = "…")` — which
addresses a member of a *string*, since Cosmos stores a `DateTime` as ISO text. Valid query, runs fine,
answers the wrong question silently.

**The fix.** `Birko.Data.Expressions.DateTruncation.Rewrite` turns `x.Col.Date <op> constant` into a
half-open range over the raw member, wired into the six CosmosDB entry points beside
`SpanContains.Rewrite`. `CosmosFilterMatrixLiveTests`: **26 of 27 → 27 of 27**.

**Judgement calls.**

- **All six operators, not just `==`.** The task explicitly warned against handling only equality, and
  the operator has to *mirror* when the member is on the right — otherwise `d < x.When.Date` inverts
  silently, which is the same defect wearing a different coat. Three reversed forms are tested.
- **Member-vs-member is left alone.** `a.Date == b.Date` compares two stored values; there is no day
  bucket to become, and rewriting it would change its meaning.
- **The rewrite lives in Core, wired only in Cosmos.** Same discipline as `SpanContains`: available to
  every backend, wired where a gap was measured. MongoDB and RavenDB both report `dateDotDate` OK.
- **The "pin the emitted SQL" criterion was met at the tree instead**, and deliberately: the rewrite is
  backend-agnostic, and a tree containing no `.Date` cannot emit `["Date"]` on any provider. Asserting
  Cosmos's SQL text would have pinned one consumer of a general transform.
- **I did NOT consolidate with the SQL implementation.** See below — that is a real duplication and the
  reasoning for leaving it is recorded rather than skipped.

**The duplication, stated plainly.** `Birko.Data.SQL.DataBase.TryBuildDateTruncatedComparison` already
performs this rewrite, for the same reason and with the same operator table — Symbio TASK-355, where
`DATE(col) = @p` compared a 10-character date against a full timestamp and matched nothing, *also*
silently. It emits `Condition` objects rather than an expression tree, so the code cannot be shared as
it stands, and both tables now carry a comment naming the other. Consolidation is possible and
attractive: run this pre-pass before the SQL parser and delete that method, since the parser would then
only ever see plain comparisons. Not done here because it is a change to heavily-tested translation code
with no behavioural gain, and doing it as a side effect of a Cosmos fix is how a regression gets in.
**Two implementations of one semantics is a known debt, not an oversight.**

**Flagged, not fixed.**

- The SQL/Core duplication above. Worth its own task if the operator tables ever need to change.
- Nothing else: the Cosmos matrix is now clean at 27/27.

## Implementation plan

_Populated by `/tasks plan TASK-224` — leave empty until then._

## Progress log

- step 2 — picked; user-directed, and it ranked first anyway: the only silent wrong answer left in the backlog.
- step 3 — verified. Diagnosis already carried in the Context (the emitted SQL was captured when the finding was filed), re-confirmed by the matrix suite reporting DIVERGE oracle=1 actual=0 before the change. Also confirmed the framework already solves this for SQL, so the semantics did not need inventing — only re-expressing at the expression level.
- step 4 — layer: Core for the rewriter (backend-agnostic, and the sibling of SpanContains/ExpressionNormalizer), Birko.Data.CosmosDB for the wiring.
- step 5 — fix in Birko.Data.Core/{Expressions/DateTruncation.cs (new), .projitems} + Birko.Data.CosmosDB/Stores/{CosmosDBStore.cs, AsyncCosmosDBStore.cs} (6 entry points); tests in Birko.Data.Core.Tests/DateTruncationTests.cs (new, 16, non-gated).
- step 5a — a test of mine was wrong and running it said so: I asserted the nullable-null case returns false, but `.Value` on a null throws in the ORIGINAL lambda too, so parity — not a chosen value — is the contract. Rewritten to assert both sides throw alike, with the realistic guarded spelling given its own test.
- step 6 — two isolating reverts. Revert A (unwire the six Cosmos entry points): 1 of 54 failed = CosmosFilterMatrixLiveTests, the only thing that pins the WIRING, since the Core tests call the rewriter directly. Revert B (gut Rewrite): 3 of 86 failed in Core = Every_operator_preserves_meaning (the whole theory), The_Date_member_is_gone_from_the_tree, The_guarded_nullable_spelling_round_trips. Contract pins, passing either way: A_comparison_of_two_dates_is_left_alone, A_predicate_with_no_Date_truncation_is_returned_unchanged, A_null_predicate_stays_null (all three pin what the rewrite must NOT do, so a no-op satisfies them — named as pins, not evidence), plus the 500 SQL / 194 SQLite tests that guard the OTHER implementation of these semantics. Fixed state: 1,168 tests green across 8 suites; matrix 27/27 live.
- step 7 — no spec change. filter-expression-translation documents the SQL `.Date` rewrite already and its globs do not reach Birko.Data.CosmosDB/Stores; the behaviour added here is the same contract, now honoured by a second backend. Recorded rather than skipped silently.
