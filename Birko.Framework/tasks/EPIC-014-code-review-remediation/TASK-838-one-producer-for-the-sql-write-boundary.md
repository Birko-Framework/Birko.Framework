---
id: TASK-838
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# One producer for "join the ambient SQL transaction, or own one"

## Context

TASK-833 added `AbstractConnector.RunAtomically` / `RunAtomicallyAsync` for chunked writes. The method joins the
ambient transaction, or opens one, publishes it as ambient, commits and runs the after-commit work, or rolls back and
discards it. The SQL bulk stores' `Atomically` / `AtomicallyAsync` (`DataBaseBulkStore`, `AsyncDataBaseBulkStore`)
repeat the same shape. That makes two producers of one boundary rule, free to drift apart, as rules 16 and 53
describe.

## Acceptance criteria

- [x] The bulk stores' `Atomically(Async)` delegate to `RunAtomically(Async)`, or the reverse, leaving one place that
      opens, publishes, commits and rolls back.
      → **Compared line by line (2026-11-07). No difference was a bug.** Two features belonged to the bulk store only,
        and both are kept:
        - it commits, then rethrows, on `EntityNotFoundException` (TASK-555);
        - it joins its own `TransactionContext`.
      → The rest matched in both copies, sync and async alike:
        - BEGIN retried through `OpenOwnedTransaction(Async)`, and the write never retried (rule 66);
        - rollback with `CancellationToken.None`;
        - after-commit work dropped on rollback;
        - the ambient transaction matched by settings id.
      → **Direction: the store delegates to the connector,** the lower layer, which chunked writes use without a store.
        - `RunAtomically(Async)` is `protected internal` and takes an optional `commitsDespite` predicate.
        - A shared `CommitOwned(Async)` commits and runs the after-commit work.
        - The stores publish their context as ambient and pass `ex => ex is EntityNotFoundException`.
        - Their own commit helpers are deleted.
- [x] The existing boundary tests (TASK-242 and TASK-833's rollback-at-chunk-k tests) stay green; every SQL importer
      suite stays green.
      → All 35 SQL importer suites are green against live SQL Server, PostgreSQL and MySQL, including the TASK-242,
        TASK-629, TASK-669 and TASK-833 boundary tests.
      → The first run hit TASK-836's in-progress `AddParameter` edit (CS0506). A rerun of the 20 affected suites was
        green.
      → The agent suspected that a throwing rollback could hide a SQL Server deadlock error. That was already measured
        false at TASK-305 (rule 66: `Rollback()` on a deadlock victim does not throw on SqlClient against 2022), so no
        task was filed.

## Out of scope

- Retrying chunked writes: their own transaction retries the BEGIN only, as the bulk store does (rule 66).

## Human test plan

N/A — fully covered by automated tests: a refactor with existing boundary tests.

## Implementation plan
