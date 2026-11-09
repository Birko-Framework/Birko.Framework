---
id: TASK-874
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-08
depends-on: [TASK-869]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQL Server: a LIKE term past 4000 characters fails with Msg 8152

## Context

Found by TASK-869 (2026-11-08), measured on SQL Server 2022: a LIKE pattern longer than 4000 characters fails with
Msg 8152 ("String or binary data would be truncated"), also when bound as `nvarchar(max)`. TASK-869 refuses the
`Ordinal` / `OrdinalIgnoreCase` forms by name past the limit (`MSSqlValueFragmentDialect.MaxLikePatternLength`); the
no-argument `Contains` / `StartsWith` / `EndsWith` still fail with the server error at about 3998 characters. Loud,
not wrong rows.

## Acceptance criteria

- [x] Reproduce (rule 54) on the no-argument forms.
      → Measured 2026-11-09 on SQL Server 2022: the limit is 4000 UTF-16 code units over the whole pattern, `%`
        wildcards and `!` escapes included (surrogate pairs: 2000 run, 2001 fail). Longest term that runs: `Contains`
        3998, `StartsWith` / `EndsWith` 3999, a term of `%`, `_`, `[` or `!` 1999. Equality has no such limit (a
        9000-character `==` / `Equals` matched, read and delete).
      → **Worse than the context said:** past the limit, count, update and delete failed with Msg 8152, but
        `ReadAsync` returned 0 rows with the matching row stored: silently wrong, not loud. Cause filed as TASK-880.
- [x] Refuse by name before anything runs, through the same limit, or translate another way if one is exact.
      → Refused: no exact alternative measured (`CHARINDEX` and `LEFT(..) =` differ from LIKE under this collation, on
        trailing spaces for one). One check, `SqlValueFragmentDialect.RequireLikePattern(pattern, form)` (the base does
        nothing), overridden by `MSSqlValueFragmentDialect` with the existing `MaxLikePatternLength = 4000`;
        `LikeConditionStrategy` calls it with the escaped pattern before binding, and TASK-869's `StringMatch` calls it
        too. The message names the form, column, length, limit and Msg 8152. Column-to-column patterns are not checked.
- [x] Tests, red before the fix.
      → `StringComparisonFilterContract`: `Contract_A_long_no_argument_term_matches_or_is_refused_by_its_limit` (9
        boundary cases each side, read, count, update and delete; refused with nothing changed on SQL Server, matched
        elsewhere) and `Contract_A_long_equality_term_matches`. `LikeConditionStrategyTests` +10 offline,
        `LikePatternLimitTests` (MSSql, 3).
      → Red on HEAD: the 5 SQL Server cases past the limit; strategy tests 8 of 28 with the change removed. Green:
        contract 303 of 303 on SQL Server, 12 of 12 long-term tests on SQLite, PostgreSQL and MySQL.
      → Live, `BIRKO_REQUIRE_LIVE=1`, every importer of `Birko.Data.SQL` / `Birko.Data.SQL.MSSql`: green, excluding
        TASK-864's and TASK-866's in-progress tests. Some SQL Server runs hit duplicate keys on shared tables from the
        parallel agents and passed on rerun. TimescaleDB not pointed at its container.
      → **Found, filed:** TASK-880 (P1: on SQL Server an error raised while rows stream turns a read into an empty
        result).

## Out of scope

- Other providers (MySQL and PostgreSQL handled 10,000-class patterns, measured).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
