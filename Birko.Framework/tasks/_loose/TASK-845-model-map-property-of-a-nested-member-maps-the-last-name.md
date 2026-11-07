---
id: TASK-845
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-843]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A fluent SQL mapping of a nested member maps the field named by the last segment only

## Context

Found by TASK-843 (2026-11-07), from reading the code; not yet reproduced. It is the third copy of the flattening
TASK-840 (Cosmos DB view filters) and TASK-843 (view projections) fixed.
- `Birko.Models.SQL/Mapping/ModelMap.GetMemberName` keeps only the last member name, so
  `map.Property(x => x.Sub.X)` configures column `X`, as if `x.X` had been written.
- If the entity also has a top-level `X`, the configuration (type, length, index, uniqueness) lands on the wrong
  column. If it doesn't, it lands on a name the table may not have. Either way nothing is raised (rules 41–44).
- TASK-843 made `ViewDefinitionBuilder.GetMemberPath` the producer of a member path for views; a SQL table has no
  nested member to map.

## Acceptance criteria

- [ ] Reproduced first: a fluent mapping of `x => x.Sub.X` on an entity that also has a top-level `X`. Record which
      column the configuration lands on.
- [ ] A nested member in a fluent mapping is refused by name when the mapping is registered (SQL tables have no nested
      members). A single-segment member keeps its behaviour byte for byte. Rule 16: reuse the member-path producer
      from TASK-843 if it fits, or say why not.
- [ ] Regression tests red before the fix; the SQL importer suites stay green.

## Out of scope

- View definitions (TASK-843, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
