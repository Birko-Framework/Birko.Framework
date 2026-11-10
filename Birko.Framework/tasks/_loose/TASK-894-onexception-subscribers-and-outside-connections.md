---
id: TASK-894
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-875]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `OnException` subscribers may never run; statements outside the connector are not announced

## Context

Found by TASK-875 (2026-11-09), by reading the code; not reproduced:
- **Consumer `OnException` subscribers never run:** each provider registers its own handler first in its constructor,
  and that handler always throws, so a later subscriber is not reached (rule 25).
- **Statements on connections outside the connector are not announced to `OnExecute`:** `Birko.Data.Migrations.SQL`,
  `Migrations.TimescaleDB`, `SQL.View.Migrations` and `BackgroundJobs.SQL`'s lock provider open their own connections.
  They need an announce entry point those projects can reach.

## Acceptance criteria

- [x] Reproduce each (rule 54).
      → Reproduced 2026-11-10 on SQLite and live PostgreSQL 16, MySQL 8.4, SQL Server 2022 (+ TimescaleDB for item 2):
        (1) a consumer `OnException` subscriber was called 0 times for every failure (reads, creates, filter updates,
        bulk creates, sync and async; a write to a dropped table; a count of a missing table) — each provider's
        constructor subscribed its own always-throwing handler first. (2) 0 statements announced from migrations (store
        table, raw scripts, `CollectionExists`, `RenameField`, data migrator), view migrations, the TimescaleDB migration's
        emitters and probes, and the job lock provider.
- [x] Every `OnException` subscriber runs, whatever the provider's own handler does; the outside statements are
      announced through `AnnounceStatement`/`AnnounceCommand`, or each gap is recorded with its reason.
      → (1) `InitException` is non-virtual and `[DoesNotReturn]` (rule 12): it calls the new `protected virtual
        ReportCommandException` (providers override it with `EnsureSchemaAndReport` instead of subscribing), then runs
        every `OnException` subscriber contained through `RaiseEach` (failures recorded on `SubscriberFailures`), then
        rethrows the same object with its stack, so `IsTransientException` / `IsNotAppliedException`,
        `IsMissingTableExceptionChain`, unique-violation and cancellation filters still match (rule 9). A subscriber can no
        longer swallow a failure. The unreachable CR-M134 `faulted` flags removed (rule 53).
      → (2) One producer, `AbstractConnector.AnnounceCommand(DbCommand)` (now public), called before each execute by
        `SqlMigrationStore`, `SqlSchemaBuilder`, `SqlDataMigrator`, `SqlScriptMigration`, `TimescaleDBMigration`,
        `ViewMigrationExtensions` and `SqlJobLockProvider`. Gaps recorded: `SqlMigrationContext.Raw(action)` hands out the
        raw connection; `SqlHealthCheck` holds no connector; BEGIN/COMMIT/ROLLBACK and SQLite's journal PRAGMA (TASK-875).
        **Breaking for connector subclasses:** `InitException` no longer virtual.
- [x] Tests, red before the fix.
      → `Task894OnExceptionSubscriberTests` (four providers, 10 failure shapes; missing-table count still 0; a SQLite retry
        test showing the retry filter still matches — red 2/2 without the change), `Task894MigrationAnnounceTests`,
        `Task894ViewMigrationAnnounceTests`, `Task894TimescaleMigrationAnnounceTests`, `Task894LockAnnounceTests`. Red:
        13/13 SQLite + 33/33 live, 8/8, 8/8, 1/1, 3/3. Green live.
      → All 35 importers live on a HEAD + patch snapshot: green apart from a MySQL healing-class flake under parallel
        classes that fails on HEAD too (3/8 vs 4/8); added to TASK-883. No consumer subscribes to `OnException`.

## Out of scope

- The connector's own paths (TASK-875, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
