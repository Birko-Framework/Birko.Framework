---
id: TASK-875
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-871]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `OnExecute`: a throwing subscriber fails the operation; bulk paths may not raise it

## Context

Found by TASK-871 (2026-11-09), by reading the code; not reproduced.
- **A throwing subscriber:** the providers' built-in `OnException` handler (`EnsureSchemaAndReport`) passes the
  subscriber's exception back to the caller, so a logging subscriber that throws fails the read or write. Rule 25 says a
  diagnostic channel must survive its own subscriber.
- **Bulk paths:** PostgreSQL binary COPY, MySQL bulk insert and other paths that bypass the command runners may not
  raise `OnExecute` at all.
- **`ExecuteReader` errors** (TASK-880): an error other than a missing table, thrown by `ExecuteReader`, goes straight
  to the caller without `InitException` / `OnException`, while errors from `Read()` pass through them.

## Acceptance criteria

- [x] Reproduce each (rule 54), per provider, sync and async. Record which operations fail on a throwing subscriber,
      and which statements never reach `OnExecute`.
      → Measured 2026-11-09 on SQLite and live PostgreSQL 16, MySQL 8.4, SQL Server 2022, identical on all four:
        - A throwing subscriber failed 21 operations with its own exception and nothing written (create, reads, count,
          update, filter update Set and Action, delete, filter delete, schema ensure, multi-row insert; sync and async);
          a second subscriber never ran. The announcement ran inside the runner's `try`, and the provider's `OnException`
          handler rethrew it.
        - Never announced: store `Create` / `Update` / `Delete` of a collection on all four (incl. PostgreSQL binary COPY
          and `SqlBulkCopy`); the base multi-row insert announced 1 of 3 rows; `IIndexManager` create/exists 0. DDL and
          schema ensure were announced.
        - `ExecuteReader` errors (a missing column) reached the caller without the statement on
          `Exception.Data["Birko.CommandText"]`, unlike `Read()` and count errors. Types were right either way.
- [x] A throwing subscriber no longer changes the operation's outcome (rule 25), and every statement sent reaches
      `OnExecute` through `AnnounceCommand`, or the gap is recorded with its reason.
      → Containment (rule 25): `RaiseDiagnostic`'s loop became a shared `RaiseEach`; each `OnExecute` subscriber runs in
        its own `try`, a failure is recorded on `SubscriberFailures` under `OnExecute`, as the other diagnostic channels
        do.
      → `AnnounceCommand(cmd, out text)` stays the one producer of the text; `AnnounceCommand(DbCommand)` (builds the text
        only with a subscriber, so unsubscribed bulk writes pay nothing per row) for bulk rows and batches, and
        `AnnounceStatement(string)` for COPY and `SqlBulkCopy` (`COPY … FROM STDIN (FORMAT BINARY)`,
        `INSERT BULK [table] (columns)`; the rows travel as data). Multi-row insert announces every row; `SqlIndexManager`
        announces its statements.
      → `ExecuteReader` errors other than a missing table go through `InitException` like `Read()` errors (rule 26's
        missing-table swallow unchanged). Rule 9: types kept, only `Exception.Data` added; `IsTransientException`,
        `IsMissingTableException(Chain)`, `IsIndexAlreadyExistsException` still match.
      → Recorded gaps (`Birko.Data.SQL/CLAUDE.md`): BEGIN / COMMIT / ROLLBACK are driver calls; SQLite's journal-mode
        PRAGMA is left out on purpose (announcing it attributed it to whichever operation opened the connection and broke
        "refused before any statement" tests), pinned by a test; statements on connections outside the connector
        (migrations, TimescaleDB and view migrations, the job lock provider) — TASK-894.
- [x] Tests, red before the fix.
      → `Task875OnExecuteContainmentTests` (28 operations × 2 theories plus the `ExecuteReader` case per provider, plus a
        SQLite PRAGMA pin): red 152 of 228 with the production hunks reversed (the 76 passing pin already-announced
        statements), green 229/229 live.
      → Live importers: green apart from other tasks' in-progress tests at the time (TASK-873) and TimescaleDB without
        its host variable. No consumer subscribes to `OnExecute` or `OnException`.
      → **Found, filed:** TASK-894 (consumer `OnException` subscribers never run, by reading; statements on connections
        outside the connector are not announced).

## Out of scope

- The text's form (TASK-871, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
