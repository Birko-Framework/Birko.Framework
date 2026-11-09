---
id: TASK-880
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL Server: an error raised while rows are produced turns a read into an empty result

## Context

Found by TASK-874 (2026-11-09), measured live on SQL Server 2022: `ReadAsync(x => x.Hits / (x.Hits - x.Hits) == 0)`
returns 0 rows, while `CountAsync` with the same filter throws "Divide by zero". A LIKE pattern past 4000 characters did
the same before TASK-874 refused it. `AbstractAsyncConnector`'s reader loop stops as soon as `HasRows` is false, so an
error that arrives after the result's metadata is never read. Rule 26: a reader that answers an error with an empty
result gives a wrong answer. Any runtime error in a read (overflow, conversion, divide by zero) is silently an empty
list.

## Acceptance criteria

- [x] Reproduce (rule 54) per provider, sync and async, reads, paged reads, view reads, the SELECT before
      `Update(filter, Action)`: divide by zero, conversion and overflow errors. Record which return empty.
      → Measured 2026-11-09 live, sync and async, on reads, paged reads, `Update(filter, Action)`'s SELECT, on-the-fly
        view reads and a grouped SUM view (on-the-fly and persistent), with the count as baseline. **Only SQL Server
        swallowed:** divide by zero and an overflow before any row returned 0 rows on all 8 read paths while the count
        threw; an overflow after a row returned 0 rows or threw by scan order; a SUM overflow in a grouped view returned
        0 rows while the count answered 1. PostgreSQL and MySQL throw (MySQL and SQLite give NULL for divide by zero,
        no error: TASK-888). Conversion errors are not reachable from a filter since TASK-860.
- [x] Every reader path drains the result so a server error reaches the caller (one producer for the read loop).
      → Cause: the reader loops stopped when `HasRows` was false, and SQL Server sends the error after the metadata. The
        loop is now `Read()` until false, with no `HasRows` check: one loop per sync/async (`RunReaderCommand`'s owned-
        connection copy now calls `RunReaderCommandOn`). Error handling is unchanged; the only thing swallowed is still
        a missing table at `ExecuteReader` (rule 26). A `NextResult()` drain was tried and removed: every read is one
        statement and `Read()` raises the error, so it was unreachable (rule 53).
- [x] Tests, red before the fix.
      → `tests/Birko.Data.SQL.Views.Tests/Task880ReadErrorTests.cs`: 36 cases per provider, 144 in all; a read answers
        what the count answers (count throws → read throws, update writes nothing). Red with the connector hunks
        reverse-applied: 24 of 144, all SQL Server. Green 144/144.
      → Live, one run at a time: 31 of 35 importer suites green; the four provider suites green apart from TASK-870's
        in-progress contract (not this change).
      → **Found, filed:** TASK-887 (P1: a grouped `SqlViewStore` read throws a cast exception on all four providers);
        TASK-888 (divide by zero in a filter is NULL on SQLite and MySQL, C# throws; decision needed); a non-missing-
        table error from `ExecuteReader` skips `InitException` / `OnException`, added to TASK-875.

## Out of scope

- Which filters are translated (TASK-860, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
