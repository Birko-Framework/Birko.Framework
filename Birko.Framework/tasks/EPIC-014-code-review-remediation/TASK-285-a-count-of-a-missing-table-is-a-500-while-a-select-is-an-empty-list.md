---
id: TASK-285
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P1
assignee: unassigned
created: 2026-08-30
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A `COUNT` of a missing table throws while a `SELECT` of the same table returns empty — the one read that answers 500

## Context

Raised by Symbio TASK-602, where a fresh deployment intermittently answered **500** on a first
read/write — roughly **1 bring-up in 5**, with no bad input and nothing wrong in the data. Two occurrences
were captured, on two unrelated tables in two modules (`LeaveRequests` in HR, `CustomerAccounts` in
Customers), each with the same stack:

```
System.Exception: SELECT count(*) as count FROM "CustomerAccounts" …
  ---> SqliteException: SQLite Error 1: 'no such table: CustomerAccounts'.
        at AbstractConnector.EnsureSchemaAndReport(…)
        at SqLiteConnector.SqLiteConnector_OnException(…)
```

**Seven hypotheses were tested against a live API on a deliberately wiped database and none reproduced
the race** (detail in Symbio TASK-602). The root cause — how a store reaches a `COUNT` believing it is
initialised while the table is absent — is **still unnamed**, and this task does not claim to name it.

What it fixes is a **real inconsistency that is independently wrong**, and that turns that unknown
condition into an HTTP 500 rather than a correct answer.

## The inconsistency

| statement shape | missing table | caller sees |
|---|---|---|
| `SELECT …` (reader) | `catch when (IsMissingTableException(ex))` → `yield break` — `AbstractConnector.cs:452`, async `AbstractAsyncConnector.cs:378` | empty result |
| `SELECT count(*)` (scalar) | no exemption → `InitException` → `EnsureSchemaAndReport` → **throws** | **500** |

`IsMissingTableException` exists (`AbstractConnectorBase.cs:106`) precisely so *"a reader can yield an
empty result instead of faulting"*. **A `COUNT` is a read.** The count of a table that does not exist is
`0`, exactly as the list of its rows is empty. Today the answer depends only on the statement's shape,
which is not a distinction anyone chose.

⚠ **This is NOT a proposal to soften `EnsureSchemaAndReport`.** That method throws deliberately: TASK-277
measured writes being *silently discarded* — `CreateAsync` returning a real `Guid` against a table that
was never created — and the old `DoInit(); return;` is what made that possible. **Writes must keep
reporting.** The change is scoped to the count path only.

⚠ **The predicate does not walk inner exceptions, and a naive fix will silently do nothing.**
`IsMissingTableException` tests `ex.Message` alone, while `EnsureSchemaAndReport` rethrows as
`new Exception(commandText, ex)` — whose message is the **SQL text**. So a
`catch (Exception ex) when (IsMissingTableException(ex))` placed around the count body **never matches**,
and the fix would look applied while changing nothing. The catch must walk the `InnerException` chain.

## The trade-off, stated rather than assumed

Returning `0` **hides an anomaly instead of explaining it**: if something really is leaving a table
invisible to a live statement, `0` is a quiet wrong answer where `500` is a loud one.

The counter-argument, and why this is still right: the system is **already** inconsistent about exactly
this condition — the reader has hidden it since TASK-211 — so the same missing table today yields an
empty list on one route and a 500 on another. This does not introduce hiding; it makes the two agree,
and it removes an intermittent 500 from a first-run deployment. **Recorded as a decision, so nobody
re-derives it as an oversight.**

## Acceptance criteria

- [x] `SelectCount` (sync, `AbstractConnector_SelectCount.cs`) and `SelectCountAsync`
      (`AbstractAsyncConnector_SelectCount.cs`) return **0** when the table does not exist, instead of
      throwing. ⚠ **Both** — a fix to one half is the recurring shape of defect in this area.
- [x] The catch walks the **inner-exception chain**, and there is a test that would fail against a
      message-only check. Without it the fix is inert (see above).
- [x] Provider phrasing keeps working: the chain walk must call the **virtual**
      `IsMissingTableException`, so PostgreSQL's `relation "x" does not exist`, MySQL's `doesn't exist`
      and MSSQL's `Invalid object name` are all still recognised through their overrides.
- [x] ⚠ **Writes are unchanged.** A test pins that an `Insert`/`Create` against a missing table still
      reports — TASK-277's defect must not be reopened by this.
- [x] ⚠ **Reads are unchanged.** The existing reader `yield break` behaviour keeps its tests.
- [x] Mutation-proven both ways: revert the catch → the count test goes red; revert to a message-only
      predicate → the inner-chain test goes red.

## Out of scope

- **The root cause of the missing table** — Symbio TASK-602 keeps hunting it. This closes the symptom
  class; it does not close that question, and the two must not be conflated.
- **`EnsureSchemaAndReport`'s throw-on-write** — TASK-277, deliberately kept.
- **Making the reader path call `DoInit`** — today a reader on a missing table returns empty *without*
  creating the table. Arguably inconsistent with the count path now creating it, but it is a separate
  decision with its own blast radius.

## Human test plan

- [x] N/A — the verdict is a return value and an exception type, both of which an assertion measures
      better than a person.

## Implementation plan

_Populated by `/tasks plan TASK-285` — leave empty until then._
