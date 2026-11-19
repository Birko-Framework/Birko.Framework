---
id: TASK-942
parent: null
feature: null
status: todo
priority: P2
assignee: unassigned
created: 2026-11-19
depends-on: [TASK-941]
blocks: []
related: [TASK-296, TASK-285]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A SQLite read that hits "no such table" answers empty even when the table exists

## Context

Option (c) of DraCode's report (its TASK-177), filed by the user's decision, 2026-11-19, alongside [[TASK-941]].

A read through `AbstractAsyncConnector.ReadOnAsync` (and its sync twin `RunReaderCommandOn`) answers a missing-table
error with an empty result when `missingTableIsEmpty` is true (TASK-285). That is right when the table really is not
there yet. On SQLite's rollback journal, though, a pooled handle can carry a schema image older than a committed
`CREATE TABLE` (TASK-296), so "no such table" can come from a table that exists and has rows — and the read answers
empty with no error. Rule 26: a reader that answers an error with an empty result must swallow exactly one thing.

TASK-296 removed the trigger for `SqLiteSettings` callers by defaulting to WAL, and TASK-941 does the same for plain
`PasswordSettings`. The case left open is a database **not** on WAL:
- a caller who chose `SqLiteSettings.JournalMode = "DELETE"` or null;
- a file where WAL does not engage (network file systems, no shared memory); the connector records it on
  `JournalModeInEffect`, but the read does not look.

## To decide (measure first)

- Can a stale-schema "no such table" still be produced on main after TASK-941 in each of those cases? DraCode's
  deterministic recipe (pooled handle loads an empty schema; another connection creates the table and inserts; the
  stale handle is the only idle one; `BEGIN EXCLUSIVE` elsewhere) is the reproduction to start from.
- If it can: on SQLite, retry once on a fresh (unpooled) connection before answering empty, or throw when the
  connector's `JournalModeInEffect` is not `wal`, or something else. The decision is the user's; it should be priced on
  the steady state (rule 10) and must not widen the catch to anything but a missing table.
- Other providers: TASK-296 measured PostgreSQL with no analogue (server-side catalogue). Confirm the change stays
  SQLite-only.

## Acceptance criteria

- [ ] Each open case is measured on main and the result recorded, red test or falsified (rule 54).
- [ ] If a case reproduces: the read returns the rows, or fails loudly, instead of answering empty.

## Human test plan

N/A — fully covered by automated tests.
