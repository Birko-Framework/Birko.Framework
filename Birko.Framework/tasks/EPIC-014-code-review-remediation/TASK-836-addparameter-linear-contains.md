---
id: TASK-836
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
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# `AddParameter` scans the parameter collection on every add, so rendering an IN list is quadratic

## Context

This is TASK-833's Step E. The user set a threshold: spawn this task when rendering exceeds ~20% of a read. It does.

**Measured in Release on SQLite (`Task832InListCostTests`, after TASK-833):**

| ids | render (whole list) | read (chunked) |
|---|---|---|
| 1000 | 16 ms | 44–50 ms |
| 4000 | 199–205 ms | 112–224 ms |

- At one 1000-value chunk, rendering is about a third of the read.
- **Cause:** `AbstractConnectorBase.AddParameter` checks `command.Parameters.Contains(name)` before each add, and
  that is a linear scan on SqliteParameterCollection and SqlParameterCollection.
- Names from `SqlBuilderContext.GenerateParameterName` are unique by construction, because their `_{count}` suffix
  strictly increases.
- `AddParameter` is overridden in all 4 provider connectors, so a fast path needs rule 12's shape: a non-virtual
  `AddParameter` wrapper, with the per-provider value conversion behind a virtual.

## Acceptance criteria

- [x] Rendering a 4000-value IN list grows linearly: each doubling costs ≤ ~2.5×, measured with the
      `Task832InListCostTests` stage table before and after.
      → Render, Release, SQLite:

        | ids | before | after |
        |---|---|---|
        | 1000 | 34.1 ms | 1.3–2.0 ms |
        | 2000 | 127.5 ms | 3.0–3.2 ms |
        | 4000 | 340.8 ms | 22.6–28.0 ms |

        A single run's 2000→4000 step exceeds 2.5×. A best-of-5 probe (deleted afterwards) gave 0.62 / 1.05 / 2.04 /
        2.40 / 4.85 ms from 1,000 to 16,000, about 2× per doubling, so that step is noise.
      → Design (rule 12):
        - `AddParameter` is non-virtual; it still looks the name up and overwrites a duplicate.
        - The provider differences moved to `protected virtual ConvertParameterValue` (SQLite Guid → string,
          PostgreSQL DateTime kind; both call the base for enums) and `CreateParameter` (SQL Server DateTime2,
          TASK-656). MySQL's override equalled the base and is deleted.
        - `internal AddUniqueParameter` skips the lookup. Only `SqlBuilderContext.AddParameter` calls it, and only for
          the name it just generated, for the same command, with the parameter count unchanged since, so the name
          cannot exist. A misuse falls back to the lookup.
- [x] A name that is added twice still overwrites its value rather than adding a duplicate, pinned by a test, for
      the callers that rely on it (insert / update `SET`).
      → The multi-row insert re-binds the same names per row and relies on it. Pinned by
        `Task836AddParameterTests` (Birko.Data.SQL.Tests, 7 tests; a lookup counter on `TestDbParameterCollection`)
        and the SQLite twin, on a real `SqliteCommand`.
      → Red proof: with the fast path off, 100 lookups instead of 0 (2 tests red); without the count check, 1 red.
- [x] The provider overrides keep their conversions; every SQL importer suite stays green.
      → All 35 green against live PostgreSQL, MySQL and SQL Server.
        `EscapeAnomalyDiscriminationLiveTests.TASK295_a_vanished_table_heals_on_the_next_write_here_too`
        (PostgreSQL) failed once and passed on both reruns. It does not touch this change, and other agents were
        editing the tree at the time. Recorded as flaky, not explained.
      → **Breaking for subclasses:** `AddParameter` can no longer be overridden. No connector subclass exists in
        `C:\Source\Birko\Consumers` (grepped at TASK-833).

## Out of scope

- Chunking (TASK-833).

## Human test plan

N/A — fully covered by automated tests: a render-cost measurement and a duplicate-name test.

## Implementation plan
