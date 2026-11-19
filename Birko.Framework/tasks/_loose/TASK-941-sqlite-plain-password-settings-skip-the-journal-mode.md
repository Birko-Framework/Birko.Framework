---
id: TASK-941
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-19
depends-on: [TASK-296]
blocks: []
related: [TASK-942]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQLite connector built from plain `PasswordSettings` never applied the journal mode

## Context

Reported by DraCode (its TASK-177, PR birko/Dracode#9), 2026-11-19. TASK-296 fixed the stale-schema empty read by
putting every SQLite database on WAL, but `SqLiteConnector.CreateConnection` called `ApplyJournalMode` only when the
settings were `SqLiteSettings`. The stores accept plain `PasswordSettings`, and with those the file stayed on the
rollback journal, so TASK-296's defect stayed: a read could answer an empty list while the table existed with rows.
DraCode's eight KoboldLair `Sql*Repository` classes passed plain settings; only its event store used `SqLiteSettings`,
and whichever store built the cached connector first decided the mode for the shared file.

DraCode reproduced it deterministically (`DraCode.KoboldLair.Tests/Data/SqliteJournalModeTests.cs`): a pooled handle
loads the schema while the file is empty, another connection creates the table and inserts a row, the stale handle is
the only idle one in the pool, and `ReadAsync` returns an empty list with no error through `ReadOnAsync`'s
`missingTableIsEmpty` catch. Under `SqLiteSettings` (WAL) the same read returns the row.

## Decision

The user's, 2026-11-19: DraCode's option (a) — plain settings get the default journal mode — over (b), a warning on the
connector, which (a) makes unnecessary. Option (c), whether a missing-table read on SQLite should re-check rather than
answer empty, is filed separately as [[TASK-942]].

## Change

- `SqLiteSettings.DefaultJournalMode` (`"WAL"`) is the one producer of the default; `JournalMode` defaults to it.
- `SqLiteConnector.CreateConnection` applies it for plain `PasswordSettings`, through the same once-per-connector
  `ApplyJournalMode` (now taking the mode and the connection string), so it is recorded on `JournalModeInEffect` /
  `JournalModeFailure` the same way.
- A plain-settings caller has no opt-out; one who needs `DELETE` or no PRAGMA passes `SqLiteSettings`, as before.
- Spec `settings-configuration-chain`: the requirement and a scenario.

## Evidence

`JournalModeTests.Plain_password_settings_also_put_the_database_into_WAL`: red on main (the file reports `delete`),
green with the change. Birko.Data.SQL.SqLite.Tests 3193/3193, and all 18 other test projects importing
Birko.Data.SQL.SqLite green.

## Acceptance criteria

- [x] A store given plain `PasswordSettings` puts a new SQLite file into WAL, and the connector reports it.
- [x] `SqLiteSettings` behaves as before, including the `DELETE` and null opt-outs.

## Human test plan

N/A — fully covered by automated tests. DraCode's deterministic reproduction is the consumer-side check: with its
helper reverted to plain `PasswordSettings`, the read returns the row.
