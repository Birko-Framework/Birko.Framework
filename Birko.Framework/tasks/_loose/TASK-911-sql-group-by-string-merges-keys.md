---
id: TASK-911
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P1
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-909]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL Server and MySQL GROUP BY on a string merges keys by collation

## Context

Found by TASK-909 (2026-11-11, default collations, store aggregate and SQL views): grouping by a string column merges
keys that C# keeps apart, silently, so counts and sums are wrong.
- **SQL Server:** `Zebra`+`zebra`, `"a "`+`"a"`, `ß`+`ss`, and U+E000 and U+1F600 with `""` — 16 keys became 12.
- **MySQL:** `Zebra`+`zebra`, `ß`+`ss` — 16 became 14.
- SQLite and PostgreSQL not seen merging with these values (check PostgreSQL non-deterministic collations).
The aggregate contracts avoid these key values for now (comments say so). TASK-873 made `==` / IN exact the same way;
this is the GROUP BY counterpart. Also the group key the row returns (which twin's text) must be exact.

## Acceptance criteria

- [x] Reproduce per provider (rule 54): store aggregate and SQL views (on the fly, persistent), grouped by one and by
      several string keys.
      → Measured 2026-11-11, 20 keys distinct in C#, default collations. SQL Server: 13 groups (case twins, "a "/"a",
        ""/" "/U+E000/U+1F600, ß/ss, precomposed/decomposed é merged; Kind+Name 13 of 33, Kind+N 22 of 33); MySQL: 14
        (case twins, " "/U+00A0, K/Kelvin sign, ß/ss, the two é; Kind+Name 14 of 33, Kind+N 26 of 33); views the same,
        on the fly and persistent. Counts and sums added across twins, the key one twin's text. SQLite, PostgreSQL,
        TimescaleDB correct by default; a PostgreSQL non-deterministic ICU collation merged 11 keys into 10.
- [x] Group exactly in Ordinal (e.g. group by the dialect's ordinal key, returning the exact text), consistent with
      TASK-907/909's `OrdinalOrderKey`; index/plan cost measured; contracts' avoided values restored.
      → One producer `SqlValueFragmentDialect.OrdinalGroupTerms(column)`: the column, then TASK-873's exact form
        (`OrdinalText(column, bothFromRow: true)`) — rows sharing the exact key hold identical text, so only merged groups
        split and the key read back is the group's own text; SELECT list, view columns and TASK-909's ORDER BY unchanged.
        Wired into the store (`BuildAggregateQueryParts`), on-the-fly views and their count, persistent DDL and the
        migrations `ViewSqlGenerator`, which refuses a string-grouped aggregating view without a connector (as TASK-907
        for string MIN/MAX; 7 generator tests now pass a connector, one inverted, rule 56). `Portable` renders the column.
      → Cost, 100k rows / 500 keys: SQL Server indexed key 17–20 → 158–183 ms (stream → hash aggregate), unindexed
        `NVARCHAR(MAX)` 108–363 → ~210 ms; MySQL indexed 28–39 → 78–87 ms, `LONGTEXT` 150–172 → 141–145 ms; PostgreSQL
        19–23 → ~30 ms. Recorded in `Birko.Data.SQL/CLAUDE.md`. TASK-909's avoided contract keys restored.
- [x] Tests red before.
      → `AggregateStringOrderContract` grouping cases (14) plus restored row-order keys: SQL Server 30/116 and MySQL
        27/116 red before, 116/116 after on every provider; `Task911ViewGroupTests` red 12/24; `Task909ViewRowOrderTests`
        with restored keys red 16 (SQL Server) / 13 (MySQL); `Task911MigrationViewGroupTests` red 3/5;
        `Task911TimeBucketStringGroupLiveTests` guards string key + time bucket.
      → Live: all 42 importer suites of the changed shared projects green on the working tree (18,605 tests); staged HEAD + patch snapshot: SQL.Tests 1126, SQL.View.Migrations 47, SQL.Views 4051, SQLite 2480, PostgreSQL 1900, MySQL 1922, SQL Server 1956, TimescaleDB 251, InMemory 459 - all green.
      → No consumer groups by a string.
      → **Found, filed:** TASK-913 (SQL Server GROUP BY cost on bounded columns; `char` group keys; TimescaleDB continuous
        aggregates under non-deterministic collations; `TimeIntervalParser` message for "1h").

## Out of scope

- Row ordering and MIN/MAX (TASK-907, TASK-909, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
