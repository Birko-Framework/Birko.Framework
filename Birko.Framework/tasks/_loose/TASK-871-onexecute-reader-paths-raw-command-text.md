---
id: TASK-871
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL reader paths pass `OnExecute` the raw command text, without values

## Context

Found by TASK-858 (2026-11-08), by reading the code; not reproduced. The reader paths
(`AbstractConnector.cs` around line 1229, `AbstractAsyncConnector.cs` around 364) pass `OnExecute` the raw command text
with parameter placeholders. Other paths pass the diagnostic text with values substituted (`GetGeneratedQuery`,
TASK-832 and TASK-853). A subscriber logging queries therefore sees two different formats, and on reads it sees no
values.

## Acceptance criteria

- [x] Reproduce first (rule 54): subscribe to `OnExecute` and run each statement kind (read, count, insert, update,
      delete, view read), sync and async. Record what text each one receives.
      → Measured 2026-11-09 on SQLite and live on PostgreSQL, MySQL and SQL Server, identical on all four, sync and
        async: insert, update and delete by entity, filter update (`PropertyUpdate`) and delete, count and view count
        passed the text with values. Reads by id, by filter and with paging, view reads (on-the-fly and persistent) and
        the SELECT before an `Update(filter, Action)` passed placeholders (`@WHERE…`, `@LIMIT`, `@OFFSET`).
- [x] Every path passes the same form, from one producer. Decide whether that is the substituted diagnostic text or
      the raw text plus parameters, and keep TASK-832's linear cost.
      → Decided (delegated): the text with values, which most statements already passed. No consumer subscribes
        (scanned), nothing documents placeholders, one test relied on them by accident.
      → One producer, `AbstractConnector.AnnounceCommand`, at all nine call sites (five sync, four async). The text
        is built once per execution, as before, so TASK-832's cost holds; it is assigned before the subscriber runs,
        so `InitException` still gets it. `InvokeOnExecute(string)`, a second way to raise the event that nothing
        used, was removed (rule 36): breaking for an outside subclass calling it.
- [x] Tests per statement kind, red before the fix.
      → `tests/Birko.Data.SQL.Views.Tests/Task871OnExecuteTextTests.cs`: 10 cases × 4 providers (insert, read by id,
        by filter and paged, count, update, filter update Set and Action, delete, filter delete, view read and count
        in both modes, sync and async). Red before any production edit: 20 of 40. Green: 40 of 40.
      → `RuleFieldResolutionEndToEndTests` asserted `label_col = @` on a read; it checks table qualification, and now
        asserts `label_col = 'b'`.
      → All 35 importers of `Birko.Data.SQL.projitems` live: green, apart from the known PostgreSQL `With_retry_off`
        and TASK-864's in-progress contract on MySQL (not this change).
      → **Found, filed:** TASK-875 (a throwing `OnExecute` subscriber fails the operation, against rule 25; bulk
        paths may not raise `OnExecute` at all).

## Out of scope

- How values are rendered (TASK-853, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
