---
id: TASK-710
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-05]
pr: null
github-issue: null
jira-key: null
---

# SQL outbox: a stale reclaim can be won twice, and marks ignore the claim token

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. The reclaim's conditional update tests `Status == origin`, which is still true after the first processor's reclaim, so two processors reclaiming one stale row can both win and both publish. `MarkPublishedAsync` / `MarkFailedAsync` do not check `ClaimToken`, so a slow processor whose row was reclaimed overwrites it. The reclaim cap is the store's own `maxAttempts`, separate from `OutboxOptions.MaxAttempts`; the class comment still says a claim is not a lease (contradicted by TASK-451).

Evidence: Birko.EventBus.Outbox.SQL/SqlOutboxStore.cs:40,110-146,167,188.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-03, before the fix)

On SQLite, with a store that lets a second processor act between a claim's read and its update.

| Claim | Result |
|---|---|
| Two processors reclaim one stale row | **both won** (A 1, B 1) |
| A slow processor's `MarkFailed` after B reclaimed | row back to `Pending`; **C claimed it** while B was publishing |
| A publishes late, then B fails | `Published` turned back to **`Pending`** |
| Found here: a `Pending` claim read at `Attempts = 0`, claimed and failed by B in between | A's claim **won and wound `Attempts` from 1 to 0** |
| Reclaim cap separate from `OutboxOptions.MaxAttempts` | true by construction: the store's constructor `maxAttempts` (5) governed reclaims, the options' value failures |
| The class remarks say "a claim is not a lease" | stale since TASK-451 |

Also found: `InMemoryOutboxStore` did not count a reclaim at all, so an entry that killed its processor came round
forever; and it handed out its stored objects, so no token on them could tell a processor its claim was gone.

## Implementation plan

As `SqlJobQueue` did (TASK-575, TASK-158). The reclaim requires the row to be still stale (the winner moves
`ClaimedAt` to now), a `Pending` claim the attempts it read, and the abandon is the same guarded update instead of a
whole-row write. The marks need to know the claim, and `IOutboxStore` gave them only an id, so the contract changes
shape (rule 34): `OutboxEntry.ClaimToken`, `MarkPublishedAsync(OutboxEntry)`, `MarkFailedAsync(OutboxEntry, …)`, each
one update conditional on `Publishing` and the token; an entry with no token is refused. `GetPendingAsync(batchSize,
maxAttempts)` takes the processor's cap, as `MarkFailedAsync` already did; `GetPendingAsync(batchSize)` stays on both
stores with the constructor's cap. The in-memory store counts and caps reclaims and returns copies.

## Built (2026-11-03)

**Breaking:** an `IOutboxStore` implementation, or a caller marking by id, moves to the new signatures. No consumer
implements it; Symbio's `DurableOutboxTests` marks by id (told).

`tests/Birko.EventBus.Outbox.SQL.Tests/Stores/Task710ClaimFenceTests.cs` (8) and
`tests/Birko.EventBus.Outbox.Tests/Task710InMemoryClaimFenceTests.cs` (2). Five mutations (reclaim guard by status
only; pending guard without attempts; marks without the token; in-memory marks unguarded; in-memory reclaim
uncounted) were each caught. Measured live on throwaway PostgreSQL 16 and SQL Server 2022: the interleaved reclaim
won once, a slow processor's marks changed nothing, nulls were written, the cap abandoned, the late pending claim
lost. Spec `event-bus-and-messaging.md` updated (scoped; `generated-at` left).
