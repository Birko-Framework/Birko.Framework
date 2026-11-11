---
id: TASK-911
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce per provider (rule 54): store aggregate and SQL views (on the fly, persistent), grouped by one and by
      several string keys.
- [ ] Group exactly in Ordinal (e.g. group by the dialect's ordinal key, returning the exact text), consistent with
      TASK-907/909's `OrdinalOrderKey`; index/plan cost measured; contracts' avoided values restored.
- [ ] Tests red before.

## Out of scope

- Row ordering and MIN/MAX (TASK-907, TASK-909, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
