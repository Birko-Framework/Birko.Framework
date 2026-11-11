---
id: TASK-918
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-11
depends-on: [TASK-883]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Live test isolation: leftovers after TASK-883

## Context

Found by TASK-883 (2026-11-11), test infrastructure only:
- MySQL `UtcFieldFallbackLiveTests` calls `MySqlConnection.ClearAllPools()` while other classes run (rule 61), and its
  global time-zone window exposes sibling classes in the same run to a +05:30 session.
- `TestTempDirectory`'s final pool-clear and retry rely on `ProcessExit`, which vstest's testhost kill very likely never
  lets run (measured for `LiveDatabase`).
- The TimescaleDB twin of the healing-class interference was fixed with a collection (TASK-303) and could move to
  `LiveDatabase.Dedicated`.
- `docs/` and `.github/workflows/live-tests.yml` comments still describe the shared `birkoview` database.
- Old leftovers on the local servers (MySQL `birko_other`, `t152other`; SQL Server `birkoview_residue`, `birko611cs`)
  predate TASK-883; new runs no longer create them.
- Six live test projects have no CLAUDE.md (MSSql, MySQL, PostgreSQL, SQL.Views, Migrations.TimescaleDB,
  EventSourcing.SQL), so the TASK-883 note is missing there.

## Acceptance criteria

- [ ] Each measured (rule 54) and fixed or recorded.

## Out of scope

- The per-run database (TASK-883, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
